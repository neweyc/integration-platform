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
                    request.ClassName,
                    request.TimeoutSeconds), ct);

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

        group.MapGet("/{id:guid}/executions", async (
            Guid id,
            [FromQuery] int? limit,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new ListIntegrationExecutionsCommand(currentUser.TenantId, id, limit ?? 25), ct);

            return Results.Ok(result);
        });

        group.MapGet("/{id:guid}/executions/{executionId:guid}/logs", async (
            Guid id,
            Guid executionId,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new ListExecutionLogsCommand(currentUser.TenantId, id, executionId), ct);

            return Results.Ok(result);
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
                    request.CronExpression,
                    request.TimeoutSeconds), ct);

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

        group.MapPost("/{id:guid}/run", async (
            Guid id,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new RequestManualRunCommand(currentUser.TenantId, id), ct);

            return Results.Accepted($"/api/integrations/{id}/executions", result);
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
    string ClassName,
    int? TimeoutSeconds = null);

public record UpdateIntegrationRequest(
    string Name,
    string? Description,
    IntegrationStatus Status,
    string? CronExpression,
    int? TimeoutSeconds = null);
