using ControlPlane.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

public static class IntegrationEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/integrations").WithTags("Integrations").RequireAuthorization();

        group.MapPost("/", async (
            [FromBody] CreateIntegrationRequest request,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new CreateIntegrationCommand(
                    currentUser.TenantId,
                    request.Name,
                    request.Slug,
                    request.Description,
                    request.Environment,
                    request.TriggerType,
                    request.CronExpression,
                    request.ClassName), ct);

            return Results.Created($"/api/integrations/{result.Id}", result);
        });

        group.MapGet("/", async (
            [FromQuery] string? environment,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new ListIntegrationsCommand(currentUser.TenantId, environment), ct);

            return Results.Ok(result);
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new GetIntegrationCommand(currentUser.TenantId, id), ct);

            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateIntegrationRequest request,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new UpdateIntegrationCommand(
                    currentUser.TenantId,
                    id,
                    request.Name,
                    request.Description,
                    request.Status,
                    request.CronExpression), ct);

            return Results.Ok(result);
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new DeleteIntegrationCommand(currentUser.TenantId, id), ct);
            return Results.NoContent();
        });

        return app;
    }
}

public record CreateIntegrationRequest(
    string Name,
    string Slug,
    string? Description,
    string Environment,
    TriggerType TriggerType,
    string? CronExpression,
    string ClassName);

public record UpdateIntegrationRequest(
    string Name,
    string? Description,
    IntegrationStatus Status,
    string? CronExpression);
