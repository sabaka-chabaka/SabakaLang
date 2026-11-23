using SabakaLang.Extensions;
using SabakaLang.Runtime;

namespace SabakaLang.AspNet;

/// <summary>
/// The "web" native module: request/response access for scripts. It is a regular extension written with the public
/// extension API - exactly what you would write yourself. Everything the script prints becomes the response body.
/// </summary>
[SabakaModule("web", AutoImport = true, Description = "HTTP request/response access for scripts run by SabakaLang.AspNet")]
public static class WebModule
{
    private static WebContext Web(NativeContext ctx) =>
        ctx.Host as WebContext ?? throw new RuntimeException("The 'web' module can only be used while handling an HTTP request.");

    // ---- request
    [SabakaFunction("reqMethod")] public static string ReqMethod(NativeContext ctx) => Web(ctx).Http.Request.Method;
    [SabakaFunction("reqPath")] public static string ReqPath(NativeContext ctx) => Web(ctx).Http.Request.Path.Value ?? "/";

    [SabakaFunction("reqQuery")]
    public static string? ReqQuery(NativeContext ctx, string name) =>
        Web(ctx).Http.Request.Query.TryGetValue(name, out var v) ? v.ToString() : null;

    [SabakaFunction("reqHeader")]
    public static string? ReqHeader(NativeContext ctx, string name) =>
        Web(ctx).Http.Request.Headers.TryGetValue(name, out var v) ? v.ToString() : null;

    [SabakaFunction("reqBody")] public static string ReqBody(NativeContext ctx) => Web(ctx).RequestBody;

    // ---- response (call before or after print; headers are sent once the script has finished)
    [SabakaFunction("resStatus")] public static void ResStatus(NativeContext ctx, int code) => Web(ctx).StatusCode = code;
    [SabakaFunction("resType")] public static void ResType(NativeContext ctx, string contentType) => Web(ctx).ContentType = contentType;
    [SabakaFunction("resHeader")] public static void ResHeader(NativeContext ctx, string name, string value) => Web(ctx).ResponseHeaders[name] = value;

    [SabakaFunction("resRedirect")]
    public static void ResRedirect(NativeContext ctx, string url)
    {
        var w = Web(ctx);
        w.StatusCode = 302;
        w.ResponseHeaders["Location"] = url;
    }
}
