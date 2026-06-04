using Shared.Domain;

namespace ControlPlane.Features.Workflows;

public interface IWorkflowProgressionService
{
    Task MarkStartedAsync(ExecutionRecord record, WorkItem workItem, CancellationToken ct = default);
    Task AdvanceAsync(ExecutionRecord record, WorkItem workItem, bool succeeded, CancellationToken ct = default);
}

public class WorkflowProgressionService(IWorkflowRepository repository) : IWorkflowProgressionService
{
    public async Task MarkStartedAsync(ExecutionRecord record, WorkItem workItem, CancellationToken ct = default)
    {
        if (workItem.TriggerSource != TriggerSource.Workflow)
            return;

        var nodeRun = await repository.GetNodeRunForWorkItemAsync(record.TenantId, workItem.Id, ct);
        if (nodeRun is null)
            return;

        nodeRun.Status = WorkflowNodeRunStatus.Running;
        nodeRun.ExecutionRecordId = record.Id;
        nodeRun.StartedAt = record.StartedAt;
        await repository.SaveChangesAsync(ct);
    }

    public async Task AdvanceAsync(ExecutionRecord record, WorkItem workItem, bool succeeded, CancellationToken ct = default)
    {
        if (workItem.TriggerSource != TriggerSource.Workflow
            || !workItem.WorkflowRunId.HasValue
            || !workItem.WorkflowNodeId.HasValue)
        {
            return;
        }

        var nodeRun = await repository.GetNodeRunForWorkItemAsync(record.TenantId, workItem.Id, ct);
        if (nodeRun is null)
            return;

        nodeRun.ExecutionRecordId = record.Id;
        nodeRun.CompletedAt = record.CompletedAt;
        nodeRun.Status = succeeded ? WorkflowNodeRunStatus.Succeeded : WorkflowNodeRunStatus.Failed;

        if (!succeeded)
        {
            nodeRun.WorkflowRun.Status = WorkflowRunStatus.Failed;
            nodeRun.WorkflowRun.CompletedAt = record.CompletedAt ?? DateTime.UtcNow;
            await repository.SaveChangesAsync(ct);
            return;
        }

        await repository.SaveChangesAsync(ct);

        // If another branch already drove the run to a terminal state (e.g. a parallel node
        // failed), stop here — a failed workflow must not keep dispatching downstream work.
        if (nodeRun.WorkflowRun.Status != WorkflowRunStatus.Running)
            return;

        var outgoing = await repository.GetOutgoingEdgesAsync(
            record.TenantId,
            nodeRun.WorkflowRun.WorkflowDefinitionId,
            nodeRun.WorkflowNodeId,
            ct);

        foreach (var edge in outgoing)
        {
            if (!await repository.DependenciesSucceededAsync(record.TenantId, nodeRun.WorkflowRunId, edge.ToNodeId, ct))
                continue;

            var downstreamRun = await repository.GetNodeRunAsync(record.TenantId, nodeRun.WorkflowRunId, edge.ToNodeId, ct);
            if (downstreamRun is null || downstreamRun.Status != WorkflowNodeRunStatus.Pending)
                continue;

            var downstreamWorkItem = new WorkItem
            {
                TenantId = record.TenantId,
                IntegrationId = edge.ToNode.IntegrationId,
                Environment = record.Environment,
                TriggerSource = TriggerSource.Workflow,
                Status = WorkItemStatus.Pending,
                AvailableAt = DateTime.UtcNow,
                WorkflowRunId = nodeRun.WorkflowRunId,
                WorkflowNodeId = edge.ToNodeId
            };

            downstreamRun.WorkItemId = downstreamWorkItem.Id;
            downstreamRun.Status = WorkflowNodeRunStatus.Queued;
            repository.AddWorkItem(downstreamWorkItem);
        }

        var nodeRuns = await repository.GetNodeRunsAsync(record.TenantId, nodeRun.WorkflowRunId, ct);
        if (nodeRuns.All(n => n.Status == WorkflowNodeRunStatus.Succeeded))
        {
            nodeRun.WorkflowRun.Status = WorkflowRunStatus.Succeeded;
            nodeRun.WorkflowRun.CompletedAt = record.CompletedAt ?? DateTime.UtcNow;
        }

        await repository.SaveChangesAsync(ct);
    }
}
