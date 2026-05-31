using ControlPlane.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Features.Tenants;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tenants").WithTags("Tenants").RequireAuthorization();

        group.MapPost("/", async (
            [FromBody] CreateTenantRequest request,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new CreateTenantCommand(request.Name, request.Slug), ct);
            return Results.Created($"/api/tenants/{result.Id}", result);
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new GetTenantCommand(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        return app;
    }
}

public record CreateTenantRequest(string Name, string Slug);
