using System.Collections.Concurrent;
using SabakaLang.Compiler;
using SabakaLang.Engine;
using SabakaLang.Extensions;
using SabakaLang.Runtime;
using SabakaLang.StdLib;
using SabakaLang.VM;

namespace SabakaLang.AspNet;

public sealed record ScriptResult(bool Success, string Output, string? Error, bool IsCompileError);

/// <summary>
/// Compiles scripts once (cached) and runs each request in its own VM.
/// The registry and compiled programs are shared and read-only; all per-request state lives in the VM and <see cref="WebContext"/>.
/// </summary>
public sealed class SabakaWebRuntime
{
    private sealed record Entry(DateTime Stamp, CompiledProgram Program);

    private readonly SabakaEngine _engine;
    private readonly ConcurrentDictionary<string, Entry> _cache = new(StringComparer.Ordinal);
    private readonly object _compileLock = new();

    public SabakaWebOptions Options { get; }
    public string Root { get; }
    public bool ExposeErrors { get; }

    public SabakaWebRuntime(SabakaWebOptions options, string defaultRoot, bool isDevelopment)
    {
        Options = options;
        Root = Path.GetFullPath(options.Root ?? defaultRoot);
        ExposeErrors = options.ExposeErrors ?? isDevelopment;

        var registry = new ExtensionRegistry();
        StandardLibrary.Install(registry, name => name switch
        {
            "io" => options.AllowIo,
            "http" => options.AllowHttp,
            _ => true
        });
        registry.RegisterType(typeof(WebModule));
        foreach (var register in options.Registrations) register(registry);

        _engine = new SabakaEngine(standardLibrary: false, registry) { ModuleSource = new FileModuleSource(Root) };
    }

    /// <summary>Maps "/a/b" to Root/a/b.sabaka or Root/a/b/index.sabaka. Returns null if nothing matches or the path is not allowed.</summary>
    public string? ResolveScript(string urlPath)
    {
        var segments = urlPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // segments starting with '_' or '.' are private (use them for shared modules: import "_lib";)
        if (segments.Any(s => s.StartsWith('_') || s.StartsWith('.') || s.Contains('\\') || s.Contains(':'))) return null;

        var rel = string.Join(Path.DirectorySeparatorChar, segments);
        var candidates = segments.Length == 0
            ? new[] { Path.Combine(Root, "index.sabaka") }
            : new[] { Path.Combine(Root, rel + ".sabaka"), Path.Combine(Root, rel, "index.sabaka") };

        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue; // traversal guard
            if (File.Exists(full)) return full;
        }
        return null;
    }

    public ScriptResult Execute(string file, WebContext web)
    {
        var (program, errors) = GetProgram(file);
        if (program is null) return new ScriptResult(false, "", errors, IsCompileError: true);

        var output = new StringWriter();
        try
        {
            var vm = new VirtualMachine(program, _engine.Extensions,
                new VmOptions { Out = output, In = TextReader.Null, Host = web });
            vm.Run();
            return new ScriptResult(true, output.ToString(), null, false);
        }
        catch (RuntimeException ex)
        {
            var stack = ex.ScriptStack.Count > 0 ? "\n  at " + string.Join("\n  at ", ex.ScriptStack) : "";
            return new ScriptResult(false, output.ToString(), ex.Message + stack, false);
        }
        catch (Exception ex) // e.g. a bug in a native function
        {
            return new ScriptResult(false, output.ToString(), ex.GetType().Name + ": " + ex.Message, false);
        }
    }

    private (CompiledProgram? Program, string? Errors) GetProgram(string file)
    {
        var stamp = File.GetLastWriteTimeUtc(file);
        if (Options.Cache && _cache.TryGetValue(file, out var hit) && hit.Stamp == stamp) return (hit.Program, null);

        lock (_compileLock)
        {
            if (Options.Cache && _cache.TryGetValue(file, out hit) && hit.Stamp == stamp) return (hit.Program, null);

            var result = _engine.CompileFile(file);
            if (!result.Success) return (null, string.Join("\n", result.Errors));

            _cache[file] = new Entry(stamp, result.Program!);
            return (result.Program, null);
        }
    }
}
