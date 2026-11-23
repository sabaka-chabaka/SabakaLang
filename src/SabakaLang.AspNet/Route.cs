using SabakaLang.Extensions;

namespace SabakaLang.AspNet;

/// <summary>
/// Declares endpoints from a controller script (top-level code):
/// <c>Route.get("/users/{id:int}", "getUser");</c>  Patterns are normal ASP.NET route templates.
/// </summary>
[SabakaClass("Route", Module = "web", Description = "Declares HTTP routes in controller scripts")]
public sealed class Route
{
    private Route() { }

    [SabakaMethod("get")] public static void Get(NativeContext ctx, string pattern, string handler) => Add(ctx, "GET", pattern, handler);
    [SabakaMethod("post")] public static void Post(NativeContext ctx, string pattern, string handler) => Add(ctx, "POST", pattern, handler);
    [SabakaMethod("put")] public static void Put(NativeContext ctx, string pattern, string handler) => Add(ctx, "PUT", pattern, handler);
    [SabakaMethod("delete")] public static void Delete(NativeContext ctx, string pattern, string handler) => Add(ctx, "DELETE", pattern, handler);
    [SabakaMethod("patch")] public static void Patch(NativeContext ctx, string pattern, string handler) => Add(ctx, "PATCH", pattern, handler);
    [SabakaMethod("any")] public static void Any(NativeContext ctx, string pattern, string handler) => Add(ctx, "*", pattern, handler);

    // Controllers run once at startup to collect routes and again on every request; only the first run registers.
    private static void Add(NativeContext ctx, string method, string pattern, string handler)
    {
        if (ctx.Host is RouteCollector collector) collector.Add(method, pattern, handler);
    }
}
