using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Features.Workflows;

public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workflows").WithTags("Workflows").RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] string? environment,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new ListWorkflowsCommand(currentUser.TenantId, environment), ct);
            return Results.Ok(result);
        }).RequirePermission(Permission.ViewIntegrations);

        group.MapPost("/", async (
            [FromBody] CreateWorkflowRequest request,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new CreateWorkflowCommand(
                currentUser.TenantId,
                request.Name,
                request.Slug,
                request.Environment,
                request.Nodes,
                request.Edges), ct);

            return Results.Created($"/api/workflows/{result.Id}", result);
        }).RequirePermission(Permission.ManageIntegrations);

        group.MapPost("/{id:guid}/run", async (
            Guid id,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new RunWorkflowCommand(currentUser.TenantId, id), ct);
            return Results.Accepted($"/api/workflows/{id}/runs/{result.Id}", result);
        }).RequirePermission(Permission.TriggerManualRun);

        group.MapGet("/{id:guid}/runs", async (
            Guid id,
            [FromQuery] int? limit,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new ListWorkflowRunsCommand(currentUser.TenantId, id, limit ?? 25), ct);
            return Results.Ok(result);
        }).RequirePermission(Permission.ViewExecutions);

        return app;
    }
}

public record CreateWorkflowRequest(
    string Name,
    string Slug,
    string Environment,
    IReadOnlyList<WorkflowNodeInput> Nodes,
    IReadOnlyList<WorkflowEdgeInput> Edges);
