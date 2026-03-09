using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using SabakaLang.RuntimeEnvironment.Models;
using SabakaLang.Types;
using SabakaLang.VM;

namespace SabakaLang.RuntimeEnvironment;

public class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
    private static readonly IntPtr IntPtr_Zero = IntPtr.Zero;

    public static void Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                MessageBox(IntPtr.Zero, "Usage: sre <file.sar|file.sabakac>", "SabakaLang RuntimeEnvironment", 0x10);
                Environment.Exit(1);
            }
            string path = args[0];
            if (path.EndsWith(".sar", StringComparison.OrdinalIgnoreCase))
                RunSar(path);
            else
                RunBytecode(path);
        }
        catch (Exception ex)
        {
            MessageBox(IntPtr.Zero, ex.Message, "SRE Error — " + (args.Length > 0 ? System.IO.Path.GetFileName(args[0]) : ""), 0x10);
            Environment.Exit(1);
        }
    }

    private static void RunBytecode(string path)
    {
        var instructions = CompiledReader.Read(path);
        new VirtualMachine(null, null).Execute(instructions);
    }

    private static void RunSar(string sarPath)
    {
        string tmpDir = Path.Combine(
            Path.GetTempPath(),
            $"sre_{Path.GetFileNameWithoutExtension(sarPath)}_{Environment.ProcessId}");
        Directory.CreateDirectory(tmpDir);

        try
        {
            ZipFile.ExtractToDirectory(sarPath, tmpDir, overwriteFiles: true);

            var manifest = JsonSerializer.Deserialize<SarManifest>(
                File.ReadAllText(Path.Combine(tmpDir, "manifest.json")))
                ?? throw new InvalidOperationException("Invalid manifest.json");

            Console.WriteLine($"[SRE] {manifest.Name} v{manifest.Version}");
            Console.WriteLine($"[SRE] DLL entries in manifest: {manifest.Dlls.Count}");
            foreach (var d in manifest.Dlls)
                Console.WriteLine($"[SRE]   path={d.Path} alias={d.Alias ?? "(none)"}");

            var instructions = CompiledReader.Read(
                Path.Combine(tmpDir, manifest.Entry.Replace('/', Path.DirectorySeparatorChar)));

            var externals         = new Dictionary<string, Func<Value[], Value>>(StringComparer.OrdinalIgnoreCase);
            var callbackReceivers = new List<object>();

            // Папка рядом с .sar — там лежат WebView2 и другие нативные зависимости
            string sarDir = Path.GetDirectoryName(Path.GetFullPath(sarPath)) ?? "";

            foreach (var dllEntry in manifest.Dlls)
            {
                string dllPath = Path.Combine(tmpDir,
                    dllEntry.Path.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(dllPath))
                {
                    Console.Error.WriteLine($"[SRE] WARNING: DLL not found: {dllEntry.Path}");
                    continue;
                }

                bool   namespaced = dllEntry.Alias != null;
                string alias      = dllEntry.Alias
                    ?? Path.GetFileNameWithoutExtension(dllPath).ToLower();

                LoadDll(dllPath, alias, namespaced, sarDir, externals, callbackReceivers);
                Console.WriteLine($"[SRE] Loaded: {Path.GetFileName(dllPath)}" +
                    (namespaced ? $" as {alias}" : ""));
            }

            // Меняем cwd на папку с ассетами — ui.load("index.html") резолвится отсюда
            string assetsDir = Path.Combine(tmpDir, "assets");
            if (!Directory.Exists(assetsDir)) assetsDir = tmpDir;
            Directory.SetCurrentDirectory(assetsDir);

            var vm = new VirtualMachine(externals: externals);

            // InvokeCallback — позволяет DLL вызывать SabakaLang функции обратно
            foreach (var receiver in callbackReceivers)
            {
                var iface = receiver.GetType().GetInterfaces()
                    .FirstOrDefault(i => i.Name == "ICallbackReceiver");
                var prop = iface?.GetProperty("InvokeCallback");
                if (prop == null) continue;

                Func<string, string[], string> cb = (funcName, cbArgs) =>
                {
                    vm.CallFunction(funcName, cbArgs.Select(Value.FromString).ToArray());
                    return "";
                };
                prop.SetValue(receiver, cb);
            }

            vm.Execute(instructions);
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { }
        }
    }

    // ── Точная копия Compiler.LoadDll ────────────────────────────────────────
    private static void LoadDll(
        string dllPath,
        string alias,
        bool namespaced,
        string sarDir,
        Dictionary<string, Func<Value[], Value>> externals,
        List<object> callbackReceivers)
    {
        // Collectible load context — ищет зависимости в tmpDir/dlls, рядом с .sar и рядом с exe
        var alc = new SabakaDllLoadContext(dllPath, sarDir);
        Assembly asm;
        try { asm = alc.LoadFromAssemblyPath(dllPath); }
        catch (Exception ex)
        {
            alc.Unload();
            Console.Error.WriteLine($"[SRE] Load failed {Path.GetFileName(dllPath)}: {ex.Message}");
            return;
        }

        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }

        foreach (var type in types)
        {
            if (type == null) continue;

            var classAttr     = type.GetCustomAttributes()
                .FirstOrDefault(a => a.GetType().Name == "SabakaExportAttribute");
            bool isSabakaModule = type.GetInterfaces().Any(i => i.Name == "ISabakaModule");
            if (classAttr == null && !isSabakaModule) continue;

            object instance;
            try { instance = Activator.CreateInstance(type)!; }
            catch { continue; }

            if (type.GetInterfaces().Any(i => i.Name == "ICallbackReceiver"))
                callbackReceivers.Add(instance);

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = method.GetCustomAttributes()
                    .FirstOrDefault(a => a.GetType().Name == "SabakaExportAttribute");
                if (attr == null) continue;

                string exportName = (string)attr.GetType().GetProperty("Name")!.GetValue(attr)!;
                var    parameters = method.GetParameters();
                int    paramCount = parameters.Length;
                var    returnType = method.ReturnType;
                var    capInst    = instance;
                var    capMethod  = method;

                Func<Value[], Value> wrapper = args =>
                {
                    try
                    {
                        var converted = new object?[paramCount];
                        for (int i = 0; i < paramCount; i++)
                            converted[i] = ToNative(args[i], parameters[i].ParameterType);
                        return ToValue(capMethod.Invoke(capInst, converted), returnType);
                    }
                    catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
                };

                if (namespaced)
                    // "ui.title" — точно как Compiler при namespaced=true
                    externals[$"{alias}.{exportName.ToLower()}"] = wrapper;
                else
                {
                    string exportedClassName = classAttr != null
                        ? (string)classAttr.GetType().GetProperty("Name")!.GetValue(classAttr)!
                        : type.Name;
                    externals[$"{exportedClassName.ToLower()}.{exportName.ToLower()}"] = wrapper;
                }
            }
        }
    }

    // ── Конверсии — точные копии Compiler.ConvertValueToNative/ConvertNativeToValue ─
    private static object? ToNative(Value val, Type t)
    {
        if (t == typeof(int)    || t == typeof(int?))    { if (val.Type != SabakaType.Int)   throw new Exception($"Expected int, got {val.Type}");   return val.Int; }
        if (t == typeof(double) || t == typeof(double?)) { if (val.Type == SabakaType.Int) return (double)val.Int; if (val.Type == SabakaType.Float) return val.Float; throw new Exception($"Expected number, got {val.Type}"); }
        if (t == typeof(float)  || t == typeof(float?))  { if (val.Type == SabakaType.Int) return (float)val.Int;  if (val.Type == SabakaType.Float) return (float)val.Float; throw new Exception($"Expected number, got {val.Type}"); }
        if (t == typeof(bool)   || t == typeof(bool?))   { if (val.Type != SabakaType.Bool) throw new Exception($"Expected bool, got {val.Type}");   return val.Bool; }
        if (t == typeof(string))                         { if (val.Type != SabakaType.String) throw new Exception($"Expected string, got {val.Type}"); return val.String; }
        throw new NotSupportedException($"Conversion to {t} not supported");
    }

    private static Value ToValue(object? r, Type t)
    {
        if (t == typeof(void) || r == null) return Value.FromInt(0);
        if (t == typeof(int)    || t == typeof(long) || t == typeof(short) || t == typeof(byte)) return Value.FromInt(Convert.ToInt32(r));
        if (t == typeof(double) || t == typeof(float)) return Value.FromFloat(Convert.ToDouble(r));
        if (t == typeof(bool))   return Value.FromBool((bool)r);
        if (t == typeof(string)) return Value.FromString((string)r);
        throw new NotSupportedException($"Conversion from {t} not supported");
    }
}

// ── SabakaDllLoadContext — ищет зависимости в нескольких папках ──────────────
internal sealed class SabakaDllLoadContext : AssemblyLoadContext
{
    private readonly string _dllDir;       // tmpDir/dlls/
    private readonly string _sarDir;       // папка рядом с .sar (там WebView2 и т.п.)
    private readonly string _sreDir;       // папка рядом с SRE exe

    public SabakaDllLoadContext(string dllPath, string sarDir)
        : base(name: "Sabaka-" + Path.GetFileName(dllPath), isCollectible: true)
    {
        _dllDir = Path.GetDirectoryName(dllPath) ?? "";
        _sarDir = sarDir;
        _sreDir = Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
    }

    protected override Assembly? Load(AssemblyName name)
    {
        foreach (var dir in new[] { _dllDir, _sarDir, _sreDir })
        {
            string candidate = Path.Combine(dir, name.Name + ".dll");
            if (File.Exists(candidate))
            {
                try { return LoadFromAssemblyPath(candidate); }
                catch { }
            }
        }
        try { return Default.LoadFromAssemblyName(name); }
        catch { }
        return null;
    }
}