using SabakaLang.Extensions;

namespace SabakaLang.AspNet;

public sealed class SabakaWebOptions
{
    /// <summary>Folder with .sabaka scripts. Default: "site" inside the app's content root.</summary>
    public string? Root { get; set; }

    /// <summary>Cache compiled scripts and recompile when the file changes. Imported files are not tracked - restart or touch the entry script.</summary>
    public bool Cache { get; set; } = true;

    /// <summary>Show compile/runtime errors in the response. Default: only in Development.</summary>
    public bool? ExposeErrors { get; set; }

    /// <summary>The stdlib "io" (file access) and "http" (outgoing requests) modules are OFF for web scripts unless enabled here.</summary>
    public bool AllowIo { get; set; }
    public bool AllowHttp { get; set; }

    /// <summary>Request bodies larger than this are rejected with 413.</summary>
    public long MaxBodyBytes { get; set; } = 1 << 20;

    internal List<Action<ExtensionRegistry>> Registrations { get; } = [];

    /// <summary>Register a [SabakaModule] class.</summary>
    public SabakaWebOptions AddModule(Type moduleType) { Registrations.Add(r => r.RegisterType(moduleType)); return this; }

    public SabakaWebOptions AddModule<T>() => AddModule(typeof(T));

    /// <summary>Register all modules of an already referenced assembly.</summary>
    public SabakaWebOptions AddAssembly(System.Reflection.Assembly assembly) { Registrations.Add(r => r.RegisterAssembly(assembly)); return this; }

    /// <summary>Load an extension .dll from disk.</summary>
    public SabakaWebOptions AddExtensionFile(string dllPath) { Registrations.Add(r => r.RegisterAssemblyFile(dllPath)); return this; }

    /// <summary>Load every extension .dll in a folder (plugin folder).</summary>
    public SabakaWebOptions AddExtensionDirectory(string directory) { Registrations.Add(r => r.RegisterDirectory(directory)); return this; }

    /// <summary>Full access, e.g. fluent <c>registry.Register("name", m => m.Function(...))</c>.</summary>
    public SabakaWebOptions Configure(Action<ExtensionRegistry> configure) { Registrations.Add(configure); return this; }
}
