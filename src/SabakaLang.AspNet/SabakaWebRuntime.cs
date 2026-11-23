using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SabakaLang.Compiler;
using SabakaLang.Engine;
using SabakaLang.Extensions;
using SabakaLang.Runtime;
using SabakaLang.StdLib;
using SabakaLang.VM;

namespace SabakaLang.AspNet;

/// <summary>
/// Loads controller scripts, turns their <c>Route.*</c> declarations into <see cref="RouteDef"/>s and runs handlers.
/// Compiled programs and the extension registry are shared and read-only; every request gets its own VM.
/// </summary>
public sealed class SabakaWebRuntime
{
    private sealed record Entry(DateTime Stamp, CompiledProgram Program);

    private readonly SabakaEngine _engine;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, Entry> _cache = new(StringComparer.Ordinal);
    private readonly object _compileLock = new();

    public SabakaWebOptions Options { get; }
    public string Root { get; }
    public string FilesRoot { get; }
    public bool ExposeErrors { get; }

    static SabakaWebRuntime() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // windows-1251 & co.

    public SabakaWebRuntime(SabakaWebOptions options, string contentRoot, bool isDevelopment, ILogger log)
    {
        Options = options;
        _log = log;
        Root = Path.GetFullPath(options.Root ?? contentRoot);
        FilesRoot = Path.GetFullPath(options.FilesRoot ?? Path.Combine(contentRoot, "files"));
        ExposeErrors = options.ExposeErrors ?? isDevelopment;

        var registry = new ExtensionRegistry();
        StandardLibrary.Install(registry, name => name switch
        {
            "io" => options.AllowIo,
            "http" => options.AllowHttp,
            _ => true
        });
        registry.RegisterAssembly(typeof(WebModule).Assembly);
        foreach (var register in options.Registrations) register(registry);

        _engine = new SabakaEngine(standardLibrary: false, registry) { ModuleSource = new FileModuleSource(Root) };
    }

    /// <summary>Safe path under <see cref="FilesRoot"/> to an existing file, or null.</summary>
    public string? ResolveFile(string relativePath)
    {
        var full = PathGuard.Resolve(FilesRoot, relativePath);
        return full is not null && File.Exists(full) ? full : null;
    }

    // ------------------------------------------------------------------ startup: find routes

    public IReadOnlyList<RouteDef> DiscoverRoutes()
    {
        var dir = Path.Combine(Root, Options.ControllersDirectory);
        if (!Directory.Exists(dir)) return [];

        var collector = new RouteCollector();
        foreach (var file in Directory.EnumerateFiles(dir, "*.sabaka", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            if (Path.GetFileName(file).StartsWith('_')) continue;

            var (program, errors) = GetProgram(file);
            if (program is null) throw new InvalidOperationException($"Controller '{file}' does not compile:\n{errors}");

            collector.CurrentFile = file;
            new VirtualMachine(program, _engine.Extensions,
                new VmOptions { Out = TextWriter.Null, In = TextReader.Null, Host = collector }).Run();
        }
        return collector.Routes;
    }

    // ------------------------------------------------------------------ request handling

    public async Task HandleAsync(HttpContext http, RouteDef route)
    {
        var body = "";
        if (http.Request.HasFormContentType)
            await http.Request.ReadFormAsync(http.RequestAborted);   // buffers the form; read via req.form(...)
        else if (await ReadBodyAsync(http.Request) is { } text)
            body = text;
        else
        {
            http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var request = new Request(http, body, FilesRoot);
        Response response;
        try
        {
            response = Invoke(route, new WebContext(http, request));
        }
        catch (RuntimeException ex)
        {
            var stack = ex.ScriptStack.Count > 0 ? "\n  at " + string.Join("\n  at ", ex.ScriptStack) : "";
            response = Fail(route, ex.Message + stack, ex);
        }
        catch (Exception ex) // a bug in a native function, bad compile, ...
        {
            response = Fail(route, $"{ex.GetType().Name}: {ex.Message}", ex);
        }

        await response.WriteAsync(http, this);
    }

    private Response Invoke(RouteDef route, WebContext web)
    {
        var (program, errors) = GetProgram(route.File);
        if (program is null) throw new RuntimeException("Compile error:\n" + errors);

        var output = new StringWriter();
        var vm = new VirtualMachine(program, _engine.Extensions,
            new VmOptions { Out = output, In = TextReader.Null, Host = web });

        vm.Run(); // top-level code: Route.* calls are no-ops here, functions/classes become available
        var result = vm.CallFunction(route.Handler, vm.CallFunction("currentRequest"));
        return ToResponse(result, output);
    }

    /// <summary>Response → as is; array / script object → JSON; string → text; nothing → whatever was printed, else 204.</summary>
    private static Response ToResponse(Value v, StringWriter output)
    {
        switch (v.Type)
        {
            case SabakaType.Null:
            {
                var printed = output.ToString();
                return printed.Length == 0 ? Response.FromEmpty(204) : Response.FromText(printed);
            }
            case SabakaType.Object when v.Object!.Native is Response r:
                return r;
            case SabakaType.Array:
            case SabakaType.Object:
                return Response.FromJson(Json.Stringify(v));
            case SabakaType.String:
                return Response.FromText(v.String);
            default:
                return Response.FromText(v.ToString());
        }
    }

    private Response Fail(RouteDef route, string message, Exception ex)
    {
        _log.LogError(ex, "Sabaka handler {Handler} ({File}) failed: {Message}", route.Handler, route.File, message);
        return Response.Plain(StatusCodes.Status500InternalServerError, ExposeErrors ? message : "Internal Server Error");
    }

    private async Task<string?> ReadBodyAsync(HttpRequest request)
    {
        if (request.ContentLength is 0 || HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) return "";
        if (request.ContentLength > Options.MaxBodyBytes) return null;

        using var ms = new MemoryStream();
        var buf = new byte[8192];
        int n;
        while ((n = await request.Body.ReadAsync(buf)) > 0)
        {
            if (ms.Length + n > Options.MaxBodyBytes) return null;
            ms.Write(buf, 0, n);
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    // ------------------------------------------------------------------ compile cache

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
