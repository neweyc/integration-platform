using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Workflows;

public interface IWorkflowRepository
{
    Task<bool> SlugExistsAsync(Guid tenantId, string slug, CancellationToken ct = default);
    Task<IReadOnlyList<Integration>> GetIntegrationsAsync(Guid tenantId, IReadOnlyCollection<Guid> integrationIds, CancellationToken ct = default);
    Task<WorkflowDefinition> CreateAsync(WorkflowDefinition workflow, CancellationToken ct = default);
    Task<WorkflowDefinition?> GetDefinitionAsync(Guid tenantId, Guid workflowId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowRun>> ListRunsAsync(Guid tenantId, Guid workflowId, int limit, CancellationToken ct = default);
    Task<WorkflowRun> CreateRunAsync(WorkflowRun run, IReadOnlyList<WorkItem> rootWorkItems, CancellationToken ct = default);
    Task<WorkflowNodeRun?> GetNodeRunForWorkItemAsync(Guid tenantId, Guid workItemId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowEdge>> GetOutgoingEdgesAsync(Guid tenantId, Guid workflowDefinitionId, Guid fromNodeId, CancellationToken ct = default);
    Task<bool> DependenciesSucceededAsync(Guid tenantId, Guid workflowRunId, Guid nodeId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowNodeRun>> GetNodeRunsAsync(Guid tenantId, Guid workflowRunId, CancellationToken ct = default);
    Task<WorkflowNodeRun?> GetNodeRunAsync(Guid tenantId, Guid workflowRunId, Guid nodeId, CancellationToken ct = default);
    void AddWorkItem(WorkItem workItem);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public class WorkflowRepository(AppDbContext db) : IWorkflowRepository
{
    public Task<bool> SlugExistsAsync(Guid tenantId, string slug, CancellationToken ct = default) =>
        db.WorkflowDefinitions.AnyAsync(w => w.TenantId == tenantId && w.Slug == slug, ct);

    public async Task<IReadOnlyList<Integration>> GetIntegrationsAsync(Guid tenantId, IReadOnlyCollection<Guid> integrationIds, CancellationToken ct = default) =>
        await db.Integrations
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && integrationIds.Contains(i.Id))
            .ToListAsync(ct);

    public async Task<WorkflowDefinition> CreateAsync(WorkflowDefinition workflow, CancellationToken ct = default)
    {
        db.WorkflowDefinitions.Add(workflow);
        await db.SaveChangesAsync(ct);
        return workflow;
    }

    public Task<WorkflowDefinition?> GetDefinitionAsync(Guid tenantId, Guid workflowId, CancellationToken ct = default) =>
        db.WorkflowDefinitions
            .Include(w => w.Nodes)
            .Include(w => w.Edges).ThenInclude(e => e.FromNode)
            .Include(w => w.Edges).ThenInclude(e => e.ToNode)
            .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.Id == workflowId, ct);

    public async Task<IReadOnlyList<WorkflowRun>> ListRunsAsync(Guid tenantId, Guid workflowId, int limit, CancellationToken ct = default) =>
        await db.WorkflowRuns
            .AsNoTracking()
            .Include(r => r.NodeRuns).ThenInclude(n => n.WorkflowNode)
            .Where(r => r.TenantId == tenantId && r.WorkflowDefinitionId == workflowId)
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<WorkflowRun> CreateRunAsync(WorkflowRun run, IReadOnlyList<WorkItem> rootWorkItems, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        db.WorkflowRuns.Add(run);
        await db.SaveChangesAsync(ct);

        foreach (var workItem in rootWorkItems)
            db.WorkItems.Add(workItem);

        await db.SaveChangesAsync(ct);

        foreach (var nodeRun in run.NodeRuns)
        {
            var workItem = rootWorkItems.FirstOrDefault(w => w.WorkflowNodeId == nodeRun.WorkflowNodeId);
            if (workItem is null)
                continue;

            nodeRun.WorkItemId = workItem.Id;
            nodeRun.Status = WorkflowNodeRunStatus.Queued;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return (await db.WorkflowRuns
            .Include(r => r.NodeRuns).ThenInclude(n => n.WorkflowNode)
            .FirstAsync(r => r.Id == run.Id, ct));
    }

    public Task<WorkflowNodeRun?> GetNodeRunForWorkItemAsync(Guid tenantId, Guid workItemId, CancellationToken ct = default) =>
        db.WorkflowNodeRuns
            .Include(n => n.WorkflowRun)
            .Include(n => n.WorkflowNode)
            .FirstOrDefaultAsync(n => n.TenantId == tenantId && n.WorkItemId == workItemId, ct);

    public async Task<IReadOnlyList<WorkflowEdge>> GetOutgoingEdgesAsync(Guid tenantId, Guid workflowDefinitionId, Guid fromNodeId, CancellationToken ct = default) =>
        await db.WorkflowEdges
            .Include(e => e.ToNode)
            .Where(e => e.TenantId == tenantId
                     && e.WorkflowDefinitionId == workflowDefinitionId
                     && e.FromNodeId == fromNodeId)
            .ToListAsync(ct);

    public async Task<bool> DependenciesSucceededAsync(Guid tenantId, Guid workflowRunId, Guid nodeId, CancellationToken ct = default)
    {
        var dependencyIds = await db.WorkflowEdges
            .Where(e => e.TenantId == tenantId && e.ToNodeId == nodeId)
            .Select(e => e.FromNodeId)
            .ToListAsync(ct);

        if (dependencyIds.Count == 0)
            return true;

        var succeededCount = await db.WorkflowNodeRuns
            .CountAsync(n => n.TenantId == tenantId
                          && n.WorkflowRunId == workflowRunId
                          && dependencyIds.Contains(n.WorkflowNodeId)
                          && n.Status == WorkflowNodeRunStatus.Succeeded, ct);

        return succeededCount == dependencyIds.Count;
    }

    public async Task<IReadOnlyList<WorkflowNodeRun>> GetNodeRunsAsync(Guid tenantId, Guid workflowRunId, CancellationToken ct = default) =>
        await db.WorkflowNodeRuns
            .Include(n => n.WorkflowNode)
            .Where(n => n.TenantId == tenantId && n.WorkflowRunId == workflowRunId)
            .ToListAsync(ct);

    public Task<WorkflowNodeRun?> GetNodeRunAsync(Guid tenantId, Guid workflowRunId, Guid nodeId, CancellationToken ct = default) =>
        db.WorkflowNodeRuns
            .Include(n => n.WorkflowNode)
            .FirstOrDefaultAsync(n => n.TenantId == tenantId
                                   && n.WorkflowRunId == workflowRunId
                                   && n.WorkflowNodeId == nodeId, ct);

    public void AddWorkItem(WorkItem workItem) =>
        db.WorkItems.Add(workItem);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}
