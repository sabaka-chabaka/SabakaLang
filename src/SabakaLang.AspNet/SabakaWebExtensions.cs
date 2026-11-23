using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
            var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("SabakaLang");
            return new SabakaWebRuntime(options, env.ContentRootPath, env.IsDevelopment(), log);
        });
        return services;
    }

    /// <summary>
    /// Loads controllers/*.sabaka, and maps every <c>Route.get/post/...</c> declaration as a regular Minimal API endpoint.
    /// The returned group accepts the usual conventions: <c>.RequireAuthorization()</c>, <c>.RequireRateLimiting(...)</c>, <c>.WithTags(...)</c>.
    /// Routes are read once at startup; handler code is recompiled when its file changes.
    /// </summary>
    public static RouteGroupBuilder MapSabaka(this IEndpointRouteBuilder app, string prefix = "")
    {
        var runtime = app.ServiceProvider.GetRequiredService<SabakaWebRuntime>();
        var group = app.MapGroup(prefix);

        foreach (var route in runtime.DiscoverRoutes())
        {
            var r = route;
            var methods = r.Method == "*"
                ? new[] { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" }
                : new[] { r.Method };

            group.MapMethods(r.Pattern, methods, (HttpContext http) => runtime.HandleAsync(http, r));
        }
        return group;
    }
}
