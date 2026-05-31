using ControlPlane.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Features.Setup;

public static class SetupEndpoints
{
    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        // No authentication required — this is how the first user gets created.
        // The handler rejects the call if any tenant already exists.
        app.MapPost("/api/setup", async (
            [FromBody] SetupRequest request,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new SetupCommand(
                    request.TenantName,
                    request.TenantSlug,
                    request.AdminEmail,
                    request.AdminPassword), ct);

            return Results.Ok(result);
        }).WithTags("Setup");

        return app;
    }
}

public record SetupRequest(
    string TenantName,
    string TenantSlug,
    string AdminEmail,
    string AdminPassword);
