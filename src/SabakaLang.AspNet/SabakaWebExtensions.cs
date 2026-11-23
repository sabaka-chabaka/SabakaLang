using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SabakaLang.AspNet;

public static class SabakaWebExtensions
{
    /// <summary>Registers the script runtime (one shared instance).</summary>
    public static IServiceCollection AddSabaka(this IServiceCollection services, Action<SabakaWebOptions>? configure = null)
    {
        var options = new SabakaWebOptions();
        configure?.Invoke(options);
        services.AddSingleton(sp =>
        {
            var env = sp.GetRequiredService<IWebHostEnvironment>();
            return new SabakaWebRuntime(options, Path.Combine(env.ContentRootPath, "site"), env.IsDevelopment());
        });
        return services;
    }

    /// <summary>
    /// Serves .sabaka files as endpoints: <c>/</c> → index.sabaka, <c>/hello</c> → hello.sabaka (or hello/index.sabaka).
    /// Files and folders starting with '_' are never served and can hold shared modules.
    /// </summary>
    public static IEndpointConventionBuilder MapSabaka(this IEndpointRouteBuilder app, string prefix = "/")
    {
        var pattern = prefix.TrimEnd('/') + "/{**path}";
        return app.Map(pattern, async (HttpContext http) =>
        {
            var runtime = http.RequestServices.GetRequiredService<SabakaWebRuntime>();

            var file = runtime.ResolveScript(http.Request.RouteValues["path"] as string ?? "");
            if (file is null) { http.Response.StatusCode = 404; return; }

            if (http.Request.ContentLength > runtime.Options.MaxBodyBytes) { http.Response.StatusCode = 413; return; }
            var body = await ReadBodyAsync(http.Request, runtime.Options.MaxBodyBytes);
            if (body is null) { http.Response.StatusCode = 413; return; }

            var web = new WebContext(http, body);
            var result = runtime.Execute(file, web); // synchronous: the VM is a plain blocking interpreter

            if (!result.Success)
            {
                http.Response.StatusCode = 500;
                http.Response.ContentType = "text/plain; charset=utf-8";
                await http.Response.WriteAsync(runtime.ExposeErrors
                    ? (result.IsCompileError ? "Compile error:\n" : "Runtime error:\n") + result.Error
                    : "Internal Server Error");
                return;
            }

            http.Response.StatusCode = web.StatusCode;
            http.Response.ContentType = web.ContentType;
            foreach (var (k, v) in web.ResponseHeaders) http.Response.Headers[k] = v;
            await http.Response.WriteAsync(result.Output);
        });
    }

    private static async Task<string?> ReadBodyAsync(HttpRequest request, long max)
    {
        if (request.ContentLength is 0 || request.Method is "GET" or "HEAD") return "";
        using var ms = new MemoryStream();
        var buf = new byte[8192];
        int n;
        while ((n = await request.Body.ReadAsync(buf)) > 0)
        {
            if (ms.Length + n > max) return null;
            ms.Write(buf, 0, n);
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }
}
