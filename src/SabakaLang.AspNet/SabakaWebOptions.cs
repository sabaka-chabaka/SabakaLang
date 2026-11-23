using SabakaLang.Extensions;

namespace SabakaLang.AspNet;

public sealed class SabakaWebOptions
{
    /// <summary>Base folder for scripts (and their imports). Default: the app's content root.</summary>
    public string? Root { get; set; }

    /// <summary>Folder (relative to <see cref="Root"/>) with controller scripts. Files starting with '_' are not controllers (use them for shared modules).</summary>
    public string ControllersDirectory { get; set; } = "controllers";

    /// <summary>The only folder <c>Response.file</c>/<c>download</c> and <c>req.saveUpload</c> may touch. Default: "files" in the content root.</summary>
    public string? FilesRoot { get; set; }

    /// <summary>Recompile a controller when its file changes (routes themselves are read at startup).</summary>
    public bool Cache { get; set; } = true;

    /// <summary>Show error details in 500 responses. Default: only in Development.</summary>
    public bool? ExposeErrors { get; set; }

    /// <summary>stdlib "io" and "http" modules are off for web scripts unless enabled.</summary>
    public bool AllowIo { get; set; }
    public bool AllowHttp { get; set; }

    public long MaxBodyBytes { get; set; } = 1 << 20;

    internal List<Action<ExtensionRegistry>> Registrations { get; } = [];

    /// <summary>Register a [SabakaModule] class. Use this for static classes.</summary>
    public SabakaWebOptions AddModule(Type moduleType) { Registrations.Add(r => r.RegisterType(moduleType)); return this; }

    /// <summary>Same, for non-static classes (C# does not allow static classes as type arguments).</summary>
    public SabakaWebOptions AddModule<T>() => AddModule(typeof(T));

    public SabakaWebOptions AddAssembly(System.Reflection.Assembly assembly) { Registrations.Add(r => r.RegisterAssembly(assembly)); return this; }
    public SabakaWebOptions AddExtensionFile(string dllPath) { Registrations.Add(r => r.RegisterAssemblyFile(dllPath)); return this; }
    public SabakaWebOptions AddExtensionDirectory(string directory) { Registrations.Add(r => r.RegisterDirectory(directory)); return this; }
    public SabakaWebOptions Configure(Action<ExtensionRegistry> configure) { Registrations.Add(configure); return this; }
}
