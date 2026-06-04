using Shared.Domain;

namespace ControlPlane.Features.Workflows;

public record WorkflowNodeInput(string Key, string Name, Guid IntegrationId);
public record WorkflowEdgeInput(string From, string To);

public record WorkflowDefinitionResult(
    Guid Id,
    string Name,
    string Slug,
    string Environment,
    string Status,
    IReadOnlyList<WorkflowNodeResult> Nodes,
    IReadOnlyList<WorkflowEdgeResult> Edges);

public record WorkflowNodeResult(Guid Id, string Key, string Name, Guid IntegrationId);
public record WorkflowEdgeResult(string From, string To);

public record WorkflowRunResult(
    Guid Id,
    Guid WorkflowDefinitionId,
    string Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    IReadOnlyList<WorkflowNodeRunResult> Nodes);

public record WorkflowNodeRunResult(
    Guid Id,
    Guid WorkflowNodeId,
    string NodeKey,
    string NodeName,
    Guid IntegrationId,
    string Status,
    Guid? WorkItemId,
    Guid? ExecutionRecordId);

public static class WorkflowMapping
{
    public static WorkflowDefinitionResult ToResult(WorkflowDefinition workflow) =>
        new(
            workflow.Id,
            workflow.Name,
            workflow.Slug,
            workflow.Environment,
            workflow.Status.ToString(),
            workflow.Nodes.OrderBy(n => n.Key)
                .Select(n => new WorkflowNodeResult(n.Id, n.Key, n.Name, n.IntegrationId))
                .ToList(),
            workflow.Edges
                .Select(e => new WorkflowEdgeResult(e.FromNode.Key, e.ToNode.Key))
                .OrderBy(e => e.From)
                .ThenBy(e => e.To)
                .ToList());

    public static WorkflowRunResult ToResult(WorkflowRun run) =>
        new(
            run.Id,
            run.WorkflowDefinitionId,
            run.Status.ToString(),
            run.StartedAt,
            run.CompletedAt,
            run.NodeRuns
                .OrderBy(n => n.WorkflowNode.Key)
                .Select(n => new WorkflowNodeRunResult(
                    n.Id,
                    n.WorkflowNodeId,
                    n.WorkflowNode.Key,
                    n.WorkflowNode.Name,
                    n.WorkflowNode.IntegrationId,
                    n.Status.ToString(),
                    n.WorkItemId,
                    n.ExecutionRecordId))
                .ToList());
}
