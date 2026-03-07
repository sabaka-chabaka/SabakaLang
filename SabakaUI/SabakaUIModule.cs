using System.Linq;
using SabakaLang.SDK;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;

// This dll has NO WPF dependency.
// SabakaUIWpf.dll (which has WPF) is loaded lazily via Assembly.LoadFrom
// only when ui.run() is called — never at import time.

namespace SabakaUI;

public class SabakaUIModule : ISabakaModule, ICallbackReceiver
{
    private Func<string, string[], string>? _callback;

    Func<string, string[], string>? ICallbackReceiver.InvokeCallback
    {
        get => _callback;
        set => _callback = value;
    }

    private readonly record struct UiEvent(string Name, string Data);
    private static readonly UiEvent CloseEvent = new("__sabaka_close__", "");

    private readonly BlockingCollection<UiEvent> _events = new();
    private readonly ConcurrentQueue<string>     _jsQueue = new();
    private readonly Dictionary<string, string>  _handlers = new();

    private string _htmlPath  = "";
    private string _title     = "SabakaUI";
    private int    _width     = 1024;
    private int    _height    = 768;
    private bool   _resizable = true;

    // SabakaWindow stored as object — no WPF type reference in this assembly
    private object? _host;

    // SabakaUIHost methods — resolved lazily from SabakaUIWpf.dll
    private static Type?       _hostType;
    private static MethodInfo? _mStart, _mTitle, _mSize, _mResizable, _mGet, _mClose;

    private static Type GetHostType()
    {
        if (_hostType != null) return _hostType;

        // Look for SabakaUIWpf.dll in the same folder as SabakaUI.dll,
        // then fall back to the current working directory (script folder).
        string selfDir = Path.GetDirectoryName(typeof(SabakaUIModule).Assembly.Location) ?? "";
        string wpfDll  = Path.Combine(selfDir, "SabakaUIWpf.dll");

        if (!File.Exists(wpfDll))
            wpfDll = Path.Combine(Directory.GetCurrentDirectory(), "SabakaUIWpf.dll");

        if (!File.Exists(wpfDll))
            throw new FileNotFoundException(
                "SabakaUIWpf.dll not found. Searched in: " + selfDir +
                " and " + Directory.GetCurrentDirectory(), wpfDll);

        // Use a custom load context that knows where Desktop Runtime assemblies are.
        // Assembly.LoadFrom alone can't find PresentationFramework on a net9.0-windows host
        // because WPF assemblies live in the WindowsDesktop shared framework folder.
        // Pre-load WPF assemblies into the default context from the Desktop Runtime folder.
        // This must happen before loading SabakaUIWpf.dll so its dependencies resolve correctly.
        PreloadWpfAssemblies();

        var asm = Assembly.LoadFrom(wpfDll);
        _hostType = asm.GetType("SabakaUI.SabakaUIHost", throwOnError: true)!;
        return _hostType;
    }

    private static MethodInfo M(ref MethodInfo? cache, string name)
        => cache ??= GetHostType().GetMethod(name, BindingFlags.Public | BindingFlags.Static)
           ?? throw new MissingMethodException("SabakaUIHost", name);

    // ── exported API ──────────────────────────────────────────────────────────

    [SabakaExport("load")]
    public void Load(string path) => _htmlPath = path;

    [SabakaExport("on")]
    public void On(string eventName, string functionName)
        => _handlers[eventName] = functionName;

    [SabakaExport("title")]
    public void SetTitle(string t)
    {
        _title = t;
        if (_host != null)
            M(ref _mTitle, "ApplyTitle").Invoke(null, new[] { _host, t });
    }

    [SabakaExport("size")]
    public void SetSize(int w, int h)
    {
        _width = w; _height = h;
        if (_host != null)
            M(ref _mSize, "ApplySize").Invoke(null, new object[] { _host, w, h });
    }

    [SabakaExport("resizable")]
    public void SetResizable(bool allow)
    {
        _resizable = allow;
        if (_host != null)
            M(ref _mResizable, "ApplyResizable").Invoke(null, new object[] { _host, allow });
    }

    [SabakaExport("call")]
    public void JsCall(string fn, string arg)
        => _jsQueue.Enqueue("window.sabaka?._call?.(" + Js(fn) + "," + Js(arg) + ")");

    [SabakaExport("set")]
    public void JsSet(string key, string value)
        => _jsQueue.Enqueue("window.sabaka?._set?.(" + Js(key) + "," + Js(value) + ")");

    [SabakaExport("get")]
    public string JsGet(string key)
    {
        if (_host == null) return "";
        return M(ref _mGet, "JsGet").Invoke(null, new[] { _host, key }) as string ?? "";
    }

    [SabakaExport("exec")]
    public void JsExec(string js) => _jsQueue.Enqueue(js);

    [SabakaExport("closeWindow")]
    public void CloseWindow()
    {
        if (_host != null)
            M(ref _mClose, "CloseWindow").Invoke(null, new[] { _host });
    }

    [SabakaExport("run")]
    public void Run()
    {
        if (string.IsNullOrEmpty(_htmlPath))
            throw new InvalidOperationException("ui.load() must be called before ui.run()");

        string fullHtmlPath = Path.GetFullPath(_htmlPath);
        if (!File.Exists(fullHtmlPath))
            throw new FileNotFoundException("SabakaUI: HTML not found: " + fullHtmlPath);

        var windowReady = new ManualResetEventSlim(false);

        // First access to GetHostType() — loads SabakaUIWpf.dll + WPF here
        M(ref _mStart, "Start").Invoke(null, new object[]
        {
            fullHtmlPath, _title, _width, _height, _resizable,
            _jsQueue,
            (Action<string, string>)((name, data) => _events.Add(new UiEvent(name, data))),
            (Action)(() => _events.Add(CloseEvent)),
            (Action<object>)(host => { _host = host; windowReady.Set(); })
        });

        windowReady.Wait();

        foreach (UiEvent ev in _events.GetConsumingEnumerable())
        {
            if (ev.Name == CloseEvent.Name) break;
            if (_handlers.TryGetValue(ev.Name, out string? funcName) && _callback != null)
            {
                try { _callback(funcName, new[] { ev.Data }); }
                catch (Exception ex)
                { Console.Error.WriteLine("[SabakaUI] Error in '" + funcName + "': " + ex.Message); }
            }
        }
    }


    private static bool _wpfPreloaded = false;

    private static void PreloadWpfAssemblies()
    {
        if (_wpfPreloaded) return;
        _wpfPreloaded = true;

        // Find the WindowsDesktop shared framework directory
        string[] wpfAsms = new[]
        {
            "PresentationFramework",
            "PresentationCore",
            "WindowsBase",
            "System.Xaml",
        };

        string[] searchRoots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet", "shared", "Microsoft.WindowsDesktop.App"),
            @"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App",
        };

        string? fxDir = null;
        foreach (string root in searchRoots)
        {
            if (!Directory.Exists(root)) continue;
            // Pick the highest version directory
            fxDir = Directory.GetDirectories(root)
                .OrderByDescending(d => d)
                .FirstOrDefault();
            if (fxDir != null) break;
        }

        if (fxDir == null)
        {
            Console.Error.WriteLine("[SabakaUI] WARNING: WindowsDesktop.App not found, UI may fail");
            return;
        }

        foreach (string asmName in wpfAsms)
        {
            string dll = Path.Combine(fxDir, asmName + ".dll");
            if (File.Exists(dll))
            {
                try { Assembly.LoadFrom(dll); }
                catch (Exception ex)
                { Console.Error.WriteLine("[SabakaUI] Could not preload " + asmName + ": " + ex.Message); }
            }
        }
    }

    private static string Js(string s) =>
        '"' + s.Replace("\\", "\\\\")
               .Replace("\"", "\\\"")
               .Replace("\n", "\\n")
               .Replace("\r", "\\r") + '"';
}