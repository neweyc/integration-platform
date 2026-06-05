using System.Text.Json;
using ControlPlane.Features.Triggers;
using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Workflows;

public record RunWorkflowCommand(Guid TenantId, Guid WorkflowId) : ICommand<WorkflowRunResult>;

public class RunWorkflowHandler(IWorkflowRepository repository)
    : ICommandHandler<RunWorkflowCommand, WorkflowRunResult>
{
    public async Task<WorkflowRunResult> HandleAsync(RunWorkflowCommand command, CancellationToken ct = default)
    {
        var workflow = await repository.GetDefinitionAsync(command.TenantId, command.WorkflowId, ct);
        if (workflow is null)
            throw new NotFoundException($"Workflow '{command.WorkflowId}' not found.");

        if (workflow.Status != WorkflowStatus.Enabled)
            throw new ValidationException($"Workflow '{workflow.Id}' is disabled.");

        var dependencyNodeIds = workflow.Edges.Select(e => e.ToNodeId).ToHashSet();
        var rootNodes = workflow.Nodes.Where(n => !dependencyNodeIds.Contains(n.Id)).ToList();
        if (rootNodes.Count == 0)
            throw new ValidationException("Workflow has no root nodes.");

        var run = new WorkflowRun
        {
            TenantId = command.TenantId,
            WorkflowDefinitionId = workflow.Id,
            Status = WorkflowRunStatus.Running,
            StartedAt = DateTime.UtcNow
        };

        foreach (var node in workflow.Nodes)
        {
            run.NodeRuns.Add(new WorkflowNodeRun
            {
                TenantId = command.TenantId,
                WorkflowRunId = run.Id,
                WorkflowNodeId = node.Id,
                Status = WorkflowNodeRunStatus.Pending
            });
        }

        var now = DateTime.UtcNow;
        var rootWorkItems = rootNodes.Select(node => new WorkItem
        {
            TenantId = command.TenantId,
            IntegrationId = node.IntegrationId,
            Environment = workflow.Environment,
            TriggerSource = TriggerSource.Workflow,
            Status = WorkItemStatus.Pending,
            AvailableAt = now,
            WorkflowRunId = run.Id,
            WorkflowNodeId = node.Id
        }).ToList();

        var rootTriggerEvents = rootWorkItems.Select(workItem => TriggerEventRecorder.Create(
            new TriggerEventRecord(
                command.TenantId,
                workItem.IntegrationId,
                "workflow",
                TriggerSource.Workflow,
                TriggerEventOutcome.ConvertedToWork,
                now,
                EventKey: run.Id.ToString(),
                WorkItemId: workItem.Id,
                MetadataJson: JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["workflowRunId"] = run.Id,
                    ["workflowNodeId"] = workItem.WorkflowNodeId
                })))).ToList();

        var created = await repository.CreateRunAsync(run, rootWorkItems, rootTriggerEvents, ct);
        return WorkflowMapping.ToResult(created);
    }
}
