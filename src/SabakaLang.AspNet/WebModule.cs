using SabakaLang.Extensions;
using SabakaLang.Runtime;

namespace SabakaLang.AspNet;

/// <summary>
/// The "web" module (auto-imported). Its classes Request, Response, Json and Route are declared in their own files
/// with <c>Module = "web"</c>; the assembly scan merges them with this class.
/// </summary>
[SabakaModule("web", AutoImport = true, Description = "HTTP: Request, Response, Route, Json")]
public static class WebModule
{
    /// <summary>The current request. The host calls this to hand the request to the controller function.</summary>
    [SabakaFunction("currentRequest")]
    public static Request CurrentRequest(NativeContext ctx) =>
        (ctx.Host as WebContext)?.Request
        ?? throw new RuntimeException("currentRequest() is only available while handling an HTTP request.");
}
