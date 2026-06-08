using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

// Closes out execution records that are stuck in Running because the agent started them but never
// reported a terminal result — agent crash, network partition, or a lost connection during
// CompleteExecution. Without this, the poll/claim running guard sidelines the integration forever.
//
// Swept across all tenants by OrphanedExecutionReaper. Not tenant-scoped and intentionally not
// routed through the auditing dispatcher: it is a system maintenance action with no acting user.
public record ReapOrphanedExecutionsCommand(DateTime Now) : ICommand<ReapOrphanedExecutionsResult>;

public record ReapOrphanedExecutionsResult(int ReapedCount);

public interface IOrphanedExecutionRepository
{
    // All execution records currently in Running status, with their integration loaded so the
    // handler can honour each integration's configured timeout.
    Task<IReadOnlyList<ExecutionRecord>> ListRunningWithIntegrationAsync(CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, WorkItem>> GetWorkItemsAsync(
        IReadOnlyCollection<Guid> workItemIds,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public class ReapOrphanedExecutionsHandler(
    IOrphanedExecutionRepository repository,
    OrphanedExecutionReaperOptions options)
    : ICommandHandler<ReapOrphanedExecutionsCommand, ReapOrphanedExecutionsResult>
{
    public async Task<ReapOrphanedExecutionsResult> HandleAsync(
        ReapOrphanedExecutionsCommand command,
        CancellationToken ct = default)
    {
        var running = await repository.ListRunningWithIntegrationAsync(ct);
        if (running.Count == 0)
            return new ReapOrphanedExecutionsResult(0);

        var orphaned = running
            .Where(record => IsOrphaned(record, command.Now))
            .ToList();

        if (orphaned.Count == 0)
            return new ReapOrphanedExecutionsResult(0);

        var workItemIds = orphaned
            .Where(r => r.WorkItemId.HasValue)
            .Select(r => r.WorkItemId!.Value)
            .ToList();

        var workItems = await repository.GetWorkItemsAsync(workItemIds, ct);

        foreach (var record in orphaned)
        {
            var runningFor = command.Now - record.StartedAt;
            record.Status = ExecutionStatus.Failed;
            record.CompletedAt = command.Now;
            record.ErrorMessage =
                $"Execution orphaned: no terminal result reported after {(int)runningFor.TotalSeconds}s. " +
                "The agent likely crashed or lost its connection to the control plane.";
            record.UpdatedAt = command.Now;

            // Mirror the terminal status onto the work item so the poll/claim guard releases.
            // Reaping deliberately does not queue a retry: the cause is an unhealthy agent, and the
            // next scheduled tick (or a fresh manual run) is the safer recovery path.
            if (record.WorkItemId.HasValue
                && workItems.TryGetValue(record.WorkItemId.Value, out var workItem)
                && workItem.Status is WorkItemStatus.Started or WorkItemStatus.Claimed)
            {
                workItem.Status = WorkItemStatus.Failed;
                workItem.UpdatedAt = command.Now;
            }
        }

        await repository.SaveChangesAsync(ct);
        return new ReapOrphanedExecutionsResult(orphaned.Count);
    }

    // A running execution is orphaned once it has run past the integration's timeout plus a grace
    // window (so a legitimately long run is never reaped early). Integrations with no configured
    // timeout fall back to a generous default ceiling.
    private bool IsOrphaned(ExecutionRecord record, DateTime now)
    {
        var ceilingSeconds = record.Integration?.TimeoutSeconds is { } timeout and > 0
            ? timeout + options.TimeoutGraceSeconds
            : options.DefaultMaxRunningSeconds;

        return now - record.StartedAt > TimeSpan.FromSeconds(ceilingSeconds);
    }
}

public class OrphanedExecutionRepository(AppDbContext db) : IOrphanedExecutionRepository
{
    public async Task<IReadOnlyList<ExecutionRecord>> ListRunningWithIntegrationAsync(CancellationToken ct = default)
    {
        return await db.ExecutionRecords
            .Include(e => e.Integration)
            .Where(e => e.Status == ExecutionStatus.Running)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, WorkItem>> GetWorkItemsAsync(
        IReadOnlyCollection<Guid> workItemIds,
        CancellationToken ct = default)
    {
        if (workItemIds.Count == 0)
            return new Dictionary<Guid, WorkItem>();

        return await db.WorkItems
            .Where(w => workItemIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
