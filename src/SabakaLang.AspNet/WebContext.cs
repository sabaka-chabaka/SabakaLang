using Microsoft.AspNetCore.Http;

namespace SabakaLang.AspNet;

/// <summary>Host object of a request VM (<c>NativeContext.Host</c>). Native code can reach DI through <see cref="Services"/>.</summary>
public sealed class WebContext(HttpContext http, Request request)
{
    public HttpContext Http { get; } = http;
    public Request Request { get; } = request;
    public IServiceProvider Services => Http.RequestServices;
}

/// <summary>A route declared by a controller script: <c>Route.get("/users/{id}", "getUser");</c></summary>
public sealed record RouteDef(string Method, string Pattern, string Handler, string File);

/// <summary>Host object used while controllers are loaded at startup; <c>Route.*</c> calls land here.</summary>
public sealed class RouteCollector
{
    public string CurrentFile { get; set; } = "";
    public List<RouteDef> Routes { get; } = [];

    public void Add(string method, string pattern, string handler) =>
        Routes.Add(new RouteDef(method, pattern, handler, CurrentFile));
}
