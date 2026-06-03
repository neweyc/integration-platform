using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Features.Tenants;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/tenants/register", async (
            [FromBody] RegisterTenantRequest request,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new RegisterTenantCommand(
                request.TenantName, 
                request.TenantSlug, 
                request.AdminEmail, 
                request.AdminPassword), ct);
            return Results.Ok(result);
        }).WithTags("Tenants");

        var group = app.MapGroup("/api/tenants").WithTags("Tenants").RequireAuthorization();

        group.MapPost("/", async (
            [FromBody] CreateTenantRequest request,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new CreateTenantCommand(request.Name, request.Slug), ct);
            return Results.Created($"/api/tenants/{result.Id}", result);
        }).RequirePermission(Permission.ManageBilling);

        group.MapGet("/{id:guid}", async (
            Guid id,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new GetTenantCommand(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission(Permission.ManageBilling);

        return app;
    }
}

public record CreateTenantRequest(string Name, string Slug);

public record RegisterTenantRequest(
    string TenantName,
    string TenantSlug,
    string AdminEmail,
    string AdminPassword);
