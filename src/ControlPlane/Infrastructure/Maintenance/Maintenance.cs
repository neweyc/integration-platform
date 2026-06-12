using Microsoft.Extensions.Options;

namespace ControlPlane.Infrastructure.Maintenance;

public class MaintenanceOptions
{
    public const string SectionName = "Maintenance";

    // When true, the control plane refuses every state-changing request (POST/PUT/PATCH/DELETE) with 503,
    // so nothing is written to the database — no one can register a tenant, run first-run setup, or create
    // any data. Safe methods (GET/HEAD/OPTIONS) still work, so the public site, docs, read-only UI, health
    // checks, and the /api/maintenance flag keep functioning. Toggle via `Maintenance__Enabled=true` (an env
    // var; no rebuild needed). The UI reads /api/maintenance to hide its "open app / sign in" entry points.
    public bool Enabled { get; set; }
}

// Soft-launch / preview circuit breaker. See MaintenanceOptions.
public sealed class MaintenanceMiddleware(RequestDelegate next, IOptionsMonitor<MaintenanceOptions> options)
{
    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { HttpMethods.Get, HttpMethods.Head, HttpMethods.Options };

    public async Task InvokeAsync(HttpContext context)
    {
        if (options.CurrentValue.Enabled && !SafeMethods.Contains(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(
                new { error = "The service is in maintenance mode and is not accepting changes right now." });
            return;
        }

        await next(context);
    }
}

public static class MaintenanceExtensions
{
    public static IServiceCollection AddMaintenanceMode(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaintenanceOptions>(configuration.GetSection(MaintenanceOptions.SectionName));
        return services;
    }

    public static IApplicationBuilder UseMaintenanceMode(this IApplicationBuilder app) =>
        app.UseMiddleware<MaintenanceMiddleware>();

    // Public, unauthenticated flag the UI reads to hide its "open app / sign in" entry points.
    public static IEndpointRouteBuilder MapMaintenanceEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/maintenance", (IOptionsMonitor<MaintenanceOptions> options) =>
            Results.Ok(new { enabled = options.CurrentValue.Enabled }));
        return endpoints;
    }
}
