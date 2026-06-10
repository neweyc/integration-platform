using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Authorization;
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
                    request.ClassName,
                    request.Triggers,
                    request.TimeoutSeconds,
                    request.RetryMaxAttempts,
                    request.RetryBackoffSeconds,
                    request.PackageId), ct);

            return Results.Created($"/api/integrations/{result.Id}", result);
        }).RequirePermission(Permission.ManageIntegrations);

        group.MapGet("/", async (
            [FromQuery] string? environment,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new ListIntegrationsCommand(currentUser.TenantId, environment), ct);

            return Results.Ok(result);
        }).RequirePermission(Permission.ViewIntegrations);

        // Integrations whose required agent capabilities no live agent currently offers — i.e. work
        // that can't be routed anywhere right now.
        group.MapGet("/unroutable", async (
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new GetUnroutableIntegrationsCommand(currentUser.TenantId), ct);

            return Results.Ok(result);
        }).RequirePermission(Permission.ViewIntegrations);

        group.MapGet("/{id:guid}", async (
            Guid id,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new GetIntegrationCommand(currentUser.TenantId, id), ct);

            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission(Permission.ViewIntegrations);

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
        }).RequirePermission(Permission.ViewExecutions);

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
        }).RequirePermission(Permission.ViewExecutions);

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
                    request.Triggers,
                    request.TimeoutSeconds,
                    request.RetryMaxAttempts,
                    request.RetryBackoffSeconds), ct);

            return Results.Ok(result);
        }).RequirePermission(Permission.ManageIntegrations);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new DeleteIntegrationCommand(currentUser.TenantId, id), ct);
            return Results.NoContent();
        }).RequirePermission(Permission.ManageIntegrations);

        group.MapPost("/{id:guid}/run", async (
            Guid id,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new RequestManualRunCommand(currentUser.TenantId, id), ct);

            return Results.Accepted($"/api/integrations/{id}/executions", result);
        }).RequirePermission(Permission.TriggerManualRun);

        return app;
    }
}

public record CreateIntegrationRequest(
    string Name,
    string Slug,
    string? Description,
    string Environment,
    string ClassName,
    IReadOnlyList<IntegrationTriggerInput> Triggers,
    int? TimeoutSeconds = null,
    int RetryMaxAttempts = 0,
    int? RetryBackoffSeconds = null,
    Guid? PackageId = null);

public record UpdateIntegrationRequest(
    string Name,
    string? Description,
    IntegrationStatus Status,
    IReadOnlyList<IntegrationTriggerInput> Triggers,
    int? TimeoutSeconds = null,
    int RetryMaxAttempts = 0,
    int? RetryBackoffSeconds = null);
