using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Features.Health;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("")
            .WithTags("Health")
            .AllowAnonymous();

        // Liveness: the process is up and able to serve HTTP. It deliberately checks no
        // dependencies — a transient database outage must not make an orchestrator kill an
        // otherwise-healthy process. That distinction is what /readyz is for.
        group.MapGet("/healthz", () => Results.Ok(new HealthStatus("healthy")));

        // Readiness: the process can reach the database it needs to do real work. Returns 503
        // when the database is unreachable so load balancers and Kubernetes stop routing traffic
        // until it recovers, rather than sending requests that are bound to fail.
        group.MapGet("/readyz", async (AppDbContext db, CancellationToken ct) =>
        {
            var databaseReachable = await CanReachDatabaseAsync(db, ct);
            return databaseReachable
                ? Results.Ok(new ReadinessStatus("ready", "up"))
                : Results.Json(
                    new ReadinessStatus("not-ready", "down"),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        return app;
    }

    // CanConnectAsync swallows connection failures and returns false, but a misconfigured
    // connection string can still throw; treat any failure as "not reachable" so readiness
    // reports 503 instead of surfacing a 500.
    private static async Task<bool> CanReachDatabaseAsync(AppDbContext db, CancellationToken ct)
    {
        try
        {
            return await db.Database.CanConnectAsync(ct);
        }
        catch
        {
            return false;
        }
    }
}

public record HealthStatus(string Status);

public record ReadinessStatus(string Status, string Database);
