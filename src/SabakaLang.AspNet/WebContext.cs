using Microsoft.AspNetCore.Http;

namespace SabakaLang.AspNet;

/// <summary>
/// Per-request state. It is passed to the VM as <c>NativeContext.Host</c>, so any native function can reach it:
/// <c>var web = (WebContext)ctx.Host!;</c>
/// </summary>
public sealed class WebContext(HttpContext http, string requestBody)
{
    public HttpContext Http { get; } = http;
    public string RequestBody { get; } = requestBody;

    public int StatusCode { get; set; } = 200;
    public string ContentType { get; set; } = "text/plain; charset=utf-8";
    public Dictionary<string, string> ResponseHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Handy access to DI services from native code: <c>web.Services.GetRequiredService&lt;T&gt;()</c>.</summary>
    public IServiceProvider Services => Http.RequestServices;
}
