using System.Reflection;

namespace SabakaLang.Extensions;

/// <summary>
/// The set of native modules available to scripts. The compiler reads signatures from it; the VM links
/// compiled bytecode to implementations through <see cref="ResolveKey"/>.
/// </summary>
public sealed class ExtensionRegistry
{
    private readonly Dictionary<string, NativeModule> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, INativeCallable> _callables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NativeClass> _classes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<NativeExtension>> _extensionsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, NativeClass> _classesByClr = new();
    private readonly HashSet<Assembly> _registeredAssemblies = [];

    public IReadOnlyCollection<NativeModule> Modules => _modules.Values;

    public IEnumerable<NativeModule> AutoImported => _modules.Values.Where(m => m.AutoImport);

    /// <summary>Registers a module. Throws if the name, a class name or an exact overload is already taken.</summary>
    public ExtensionRegistry Register(NativeModule module)
    {
        if (_modules.ContainsKey(module.Name))
            throw new InvalidOperationException($"A module named '{module.Name}' is already registered.");

        // validate first so a failed registration leaves the registry untouched
#if SABAKA_CS15
        // C# 15 collection-expression arguments: the comparer goes straight into the HashSet.
        HashSet<string> keys = [with(StringComparer.Ordinal)];
#else
        var keys = new HashSet<string>(StringComparer.Ordinal);
#endif
        foreach (var c in module.AllCallables())
            if (!keys.Add(c.Key) || _callables.ContainsKey(c.Key))
                throw new InvalidOperationException($"Duplicate native member '{c.Key}' in module '{module.Name}'.");
        foreach (var cls in module.Classes)
            if (_classes.ContainsKey(cls.Name))
                throw new InvalidOperationException($"A native class named '{cls.Name}' is already registered (module '{module.Name}').");

        _modules[module.Name] = module;
        foreach (var c in module.AllCallables()) _callables[c.Key] = c;
        foreach (var cls in module.Classes)
        {
            _classes[cls.Name] = cls;
            if (cls.ClrType is not null) _classesByClr[cls.ClrType] = cls;
        }
        foreach (var e in module.Extensions)
        {
            if (!_extensionsByName.TryGetValue(e.Name, out var list))
                _extensionsByName[e.Name] = list = [];
            list.Add(e);
        }
        return this;
    }

    /// <summary>Builds and registers a module with the fluent <see cref="ModuleBuilder"/>.</summary>
    public ExtensionRegistry Register(string moduleName, Action<ModuleBuilder> configure)
    {
        var b = new ModuleBuilder(moduleName);
        configure(b);
        return Register(b.Build());
    }

    /// <summary>Registers every module found in <paramref name="assembly"/> ([SabakaModule] types and <see cref="ISabakaExtension"/> implementations).</summary>
    public ExtensionRegistry RegisterAssembly(Assembly assembly)
    {
        if (!_registeredAssemblies.Add(assembly)) return this;
        foreach (var m in ReflectionModuleLoader.LoadAssembly(assembly)) Register(m);
        return this;
    }

    /// <summary>Loads a .dll and registers the modules it contains.</summary>
    public ExtensionRegistry RegisterAssemblyFile(string path) =>
        RegisterAssembly(Assembly.LoadFrom(Path.GetFullPath(path)));

    /// <summary>Registers a single [SabakaModule] class.</summary>
    public ExtensionRegistry RegisterType(Type moduleType) => Register(ReflectionModuleLoader.LoadModuleType(moduleType));
    
    public ExtensionRegistry RegisterDirectory(string directory, string searchPattern = "*.dll", bool recursive = false)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Extension directory not found: {directory}");

        var files = Directory
            .EnumerateFiles(directory, searchPattern,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            AssemblyName name;
            try { name = AssemblyName.GetAssemblyName(file); }
            catch (BadImageFormatException) { continue; }
            catch (FileLoadException) { continue; }

            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == name.Name)
                && !_registeredAssemblies.Any(a => a.GetName().Name == name.Name))
                continue;

            RegisterAssemblyFile(file);
        }
        return this;
    }

    public ExtensionRegistry RegisterType<T>() => RegisterType(typeof(T));

    public NativeModule? FindModule(string name) => _modules.GetValueOrDefault(name);

    public INativeCallable? ResolveKey(string key) => _callables.GetValueOrDefault(key);

    public NativeClass? FindClass(string name) => _classes.GetValueOrDefault(name);

    public NativeClass? FindClassByClrType(Type type)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (_classesByClr.TryGetValue(t, out var c)) return c;
        return null;
    }

    public IEnumerable<NativeExtension> FindExtensions(string name) =>
        _extensionsByName.TryGetValue(name, out var l) ? l : [];
}
