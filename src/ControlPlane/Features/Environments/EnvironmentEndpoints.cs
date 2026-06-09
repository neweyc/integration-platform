using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Features.Environments;

public static class EnvironmentEndpoints
{
    public static IEndpointRouteBuilder MapEnvironmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/environments").WithTags("Environments").RequireAuthorization();

        // List the tenant's environment registry — the canonical source the UI dropdowns read from.
        group.MapGet("/", async (
            IDispatcher dispatcher, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new ListEnvironmentsCommand(currentUser.TenantId), ct);
            return Results.Ok(result);
        }).RequirePermission(Permission.ViewEnvironments);

        group.MapPost("/", async (
            [FromBody] CreateEnvironmentRequest request,
            IDispatcher dispatcher, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new CreateEnvironmentCommand(
                    currentUser.TenantId,
                    request.Name,
                    request.DisplayName,
                    request.Description,
                    request.SortOrder ?? 0,
                    request.IsDefault),
                ct);
            return Results.Ok(result);
        }).RequirePermission(Permission.ManageEnvironments);

        group.MapPut("/{name}", async (
            string name,
            [FromBody] UpdateEnvironmentRequest request,
            IDispatcher dispatcher, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new UpdateEnvironmentCommand(
                    currentUser.TenantId,
                    name,
                    request.DisplayName,
                    request.Description,
                    request.SortOrder ?? 0,
                    request.IsDefault),
                ct);
            return Results.Ok(result);
        }).RequirePermission(Permission.ManageEnvironments);

        group.MapDelete("/{name}", async (
            string name,
            IDispatcher dispatcher, ICurrentUser currentUser, CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new DeleteEnvironmentCommand(currentUser.TenantId, name), ct);
            return Results.NoContent();
        }).RequirePermission(Permission.ManageEnvironments);

        return app;
    }
}
