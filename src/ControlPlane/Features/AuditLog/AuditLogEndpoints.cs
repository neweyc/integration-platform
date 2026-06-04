using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Features.AuditLog;

public static class AuditLogEndpoints
{
    public static IEndpointRouteBuilder MapAuditLogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/audit-log").WithTags("AuditLog").RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] int? limit,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new ListAuditLogCommand(currentUser.TenantId, limit ?? 50), ct);
            return Results.Ok(result);
        }).RequirePermission(Permission.ViewAuditLog);

        return app;
    }
}
