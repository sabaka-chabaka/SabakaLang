using SabakaLang.AspNet;
using SabakaLang.Extensions;
using SabakaLang.Runtime;

namespace SabakaLang.WebSample;

/// <summary>An ordinary Sabaka extension. Because ctx.Host is the WebContext, it can see the current request.</summary>
[SabakaModule("app", AutoImport = true)]
public static class AppModule
{
    private static int _visits;

    [SabakaFunction("visits")]
    public static int Visits() => Interlocked.Increment(ref _visits);

    [SabakaFunction("clientIp")]
    public static string ClientIp(NativeContext ctx) =>
        ((WebContext)ctx.Host!).Http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // method on a built-in type: "Hello <b>" .htmlEscape()
    [SabakaExtension("string")]
    public static string HtmlEscape(string self) => System.Net.WebUtility.HtmlEncode(self);
}
