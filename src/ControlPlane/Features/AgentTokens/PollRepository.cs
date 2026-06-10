using System.Data;
using System.Text.Json;
using ControlPlane.Features.Integrations;
using ControlPlane.Features.Triggers;
using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public class PollRepository(AppDbContext db) : IPollRepository
{
    public async Task<IReadOnlyList<ClaimedWork>> ClaimDueScheduledAsync(
        Guid tenantId,
        string environment,
        IReadOnlyList<string> agentTags,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var triggers = await db.IntegrationTriggers
            .Include(t => t.Integration)
            .Where(t => t.TenantId == tenantId
                     && t.Type == TriggerType.Scheduled
                     && t.Enabled
                     && t.CronExpression != null
                     && t.Integration.Environment == environment
                     && t.Integration.Status == IntegrationStatus.Enabled)
            .ToListAsync(ct);

        var integrationIds = triggers.Select(t => t.IntegrationId).Distinct().ToList();
        var triggerIds = triggers.Select(t => t.Id).ToList();

        var states = await db.IntegrationScheduleStates
            .Where(s => s.TenantId == tenantId
                     && triggerIds.Contains(s.IntegrationTriggerId))
            .ToDictionaryAsync(s => s.IntegrationTriggerId, ct);

        var activeWorkItemIntegrations = await db.WorkItems
            .Where(w => w.TenantId == tenantId
                     && integrationIds.Contains(w.IntegrationId)
                     && (w.Status == WorkItemStatus.Pending
                         || w.Status == WorkItemStatus.Started
                         || (w.Status == WorkItemStatus.Claimed
                             && w.ClaimExpiresAt != null
                             && w.ClaimExpiresAt > now)))
            .Select(w => w.IntegrationId)
            .ToHashSetAsync(ct);

        var expiredClaims = await db.WorkItems
            .Include(w => w.Integration)
            .Include(w => w.IntegrationTrigger)
            .Where(w => w.TenantId == tenantId
                     && w.Environment == environment
                     && integrationIds.Contains(w.IntegrationId)
                     && w.IntegrationTriggerId != null
                     && triggerIds.Contains(w.IntegrationTriggerId.Value)
                     && w.TriggerSource == TriggerSource.Scheduled
                     && w.Status == WorkItemStatus.Claimed
                     && w.ClaimExpiresAt != null
                     && w.ClaimExpiresAt <= now)
            .ToListAsync(ct);

        // Skip integrations that already have a running execution
        var runningIntegrations = await db.ExecutionRecords
            .Where(e => e.TenantId == tenantId
                     && integrationIds.Contains(e.IntegrationId)
                     && e.Status == ExecutionStatus.Running)
            .Select(e => e.IntegrationId)
            .ToHashSetAsync(ct);

        var claimed = new List<ClaimedWork>();
        var claimExpiresAt = now.Add(claimDuration);

        foreach (var workItem in expiredClaims)
        {
            if (workItem.Integration.Status != IntegrationStatus.Enabled)
                continue;

            if (!TagSet.IsSatisfiedBy(workItem.Integration.RequiredTags, agentTags))
                continue;

            if (runningIntegrations.Contains(workItem.IntegrationId))
                continue;

            workItem.ClaimOwner = claimOwner;
            workItem.ClaimExpiresAt = claimExpiresAt;
            workItem.UpdatedAt = now;

            claimed.Add(new ClaimedWork(workItem.Integration, workItem));
        }

        var reclaimedTriggerIds = claimed
            .Select(c => c.WorkItem.IntegrationTriggerId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        foreach (var trigger in triggers)
        {
            var integration = trigger.Integration;

            if (reclaimedTriggerIds.Contains(trigger.Id))
                continue;

            if (!TagSet.IsSatisfiedBy(integration.RequiredTags, agentTags))
                continue;

            if (activeWorkItemIntegrations.Contains(integration.Id))
                continue;

            if (runningIntegrations.Contains(integration.Id))
                continue;

            states.TryGetValue(trigger.Id, out var state);

            try
            {
                var decision = ScheduleStateCalculator.Evaluate(trigger, state, now);

                if (state is null)
                {
                    state = new IntegrationScheduleState
                    {
                        TenantId = tenantId,
                        IntegrationId = integration.Id,
                        IntegrationTriggerId = trigger.Id
                    };
                    db.IntegrationScheduleStates.Add(state);
                }

                if (decision.IsDue)
                {
                    // Advance schedule state so next poll doesn't re-evaluate this period
                    state.LastDispatchedAt = decision.LastDispatchedAt;
                    state.NextRunAt = decision.NextRunAt;
                    state.UpdatedAt = now;

                    // Create and immediately claim a work item
                    var workItem = new WorkItem
                    {
                        TenantId = tenantId,
                        IntegrationId = integration.Id,
                        IntegrationTriggerId = trigger.Id,
                        Environment = environment,
                        TriggerSource = TriggerSource.Scheduled,
                        Status = WorkItemStatus.Claimed,
                        AvailableAt = now,
                        ClaimOwner = claimOwner,
                        ClaimExpiresAt = claimExpiresAt
                    };
                    db.WorkItems.Add(workItem);
                    db.TriggerEvents.Add(TriggerEventRecorder.Create(
                        new TriggerEventRecord(
                            tenantId,
                            integration.Id,
                            "scheduled",
                            TriggerSource.Scheduled,
                            TriggerEventOutcome.ConvertedToWork,
                            now,
                            IntegrationTriggerId: trigger.Id,
                            WorkItemId: workItem.Id,
                            MetadataJson: JsonSerializer.Serialize(new Dictionary<string, object?>
                            {
                                ["cronExpression"] = trigger.CronExpression
                            }))));

                    claimed.Add(new ClaimedWork(integration, workItem));
                }
                else
                {
                    state.NextRunAt = decision.NextRunAt;
                    state.UpdatedAt = now;
                }
            }
            catch
            {
                // Invalid cron expressions should be rejected by integration validation.
                // If legacy data is invalid, skip it without blocking the whole poll cycle.
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return claimed;
    }

    public async Task<IReadOnlyList<ClaimedWork>> ClaimPendingManualRunsAsync(
        Guid tenantId,
        string environment,
        IReadOnlyList<string> agentTags,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        // Find pending work items or those with an expired claim
        var claimable = await db.WorkItems
            .Include(w => w.Integration)
            .Include(w => w.IntegrationTrigger)
            .Where(w => w.TenantId == tenantId
                     && w.Environment == environment
                     && w.TriggerSource == TriggerSource.Manual
                     && (w.Status == WorkItemStatus.Pending
                         || (w.Status == WorkItemStatus.Claimed && w.ClaimExpiresAt != null && w.ClaimExpiresAt <= now)))
            .ToListAsync(ct);

        var integrationIds = claimable.Select(w => w.IntegrationId).Distinct().ToList();

        var runningIntegrations = await db.ExecutionRecords
            .Where(e => e.TenantId == tenantId
                     && integrationIds.Contains(e.IntegrationId)
                     && e.Status == ExecutionStatus.Running)
            .Select(e => e.IntegrationId)
            .ToHashSetAsync(ct);

        var claimed = new List<ClaimedWork>();
        var claimExpiresAt = now.Add(claimDuration);

        foreach (var workItem in claimable)
        {
            if (workItem.Integration.Status != IntegrationStatus.Enabled)
                continue;

            if (!TagSet.IsSatisfiedBy(workItem.Integration.RequiredTags, agentTags))
                continue;

            if (runningIntegrations.Contains(workItem.IntegrationId))
                continue;

            // Skip if another agent holds an active claim (not ours, not expired)
            if (workItem.HasActiveClaim(now) && workItem.ClaimOwner != claimOwner)
                continue;

            workItem.Status = WorkItemStatus.Claimed;
            workItem.ClaimOwner = claimOwner;
            workItem.ClaimExpiresAt = claimExpiresAt;
            workItem.UpdatedAt = now;

            claimed.Add(new ClaimedWork(workItem.Integration, workItem));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return claimed;
    }

    public Task<IReadOnlyList<ClaimedWork>> ClaimPendingWebhookRunsAsync(
        Guid tenantId,
        string environment,
        IReadOnlyList<string> agentTags,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default) =>
        ClaimPendingWorkItemsAsync(
            tenantId,
            environment,
            agentTags,
            TriggerSource.Webhook,
            claimOwner,
            claimDuration,
            now,
            ct);

    public Task<IReadOnlyList<ClaimedWork>> ClaimPendingRetryRunsAsync(
        Guid tenantId,
        string environment,
        IReadOnlyList<string> agentTags,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default) =>
        ClaimPendingWorkItemsAsync(
            tenantId,
            environment,
            agentTags,
            TriggerSource.Retry,
            claimOwner,
            claimDuration,
            now,
            ct);

    public Task<IReadOnlyList<ClaimedWork>> ClaimPendingWorkflowRunsAsync(
        Guid tenantId,
        string environment,
        IReadOnlyList<string> agentTags,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default) =>
        ClaimPendingWorkItemsAsync(
            tenantId,
            environment,
            agentTags,
            TriggerSource.Workflow,
            claimOwner,
            claimDuration,
            now,
            ct);

    public Task<IReadOnlyList<ClaimedWork>> ClaimPendingQueueRunsAsync(
        Guid tenantId,
        string environment,
        IReadOnlyList<string> agentTags,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default) =>
        ClaimPendingWorkItemsAsync(
            tenantId,
            environment,
            agentTags,
            TriggerSource.Queue,
            claimOwner,
            claimDuration,
            now,
            ct);

    public Task<IReadOnlyList<ClaimedWork>> ClaimPendingFileRunsAsync(
        Guid tenantId,
        string environment,
        IReadOnlyList<string> agentTags,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default) =>
        ClaimPendingWorkItemsAsync(
            tenantId,
            environment,
            agentTags,
            TriggerSource.File,
            claimOwner,
            claimDuration,
            now,
            ct);

    private async Task<IReadOnlyList<ClaimedWork>> ClaimPendingWorkItemsAsync(
        Guid tenantId,
        string environment,
        IReadOnlyList<string> agentTags,
        TriggerSource triggerSource,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var claimable = await db.WorkItems
            .Include(w => w.Integration)
            .Include(w => w.IntegrationTrigger)
            .Where(w => w.TenantId == tenantId
                     && w.Environment == environment
                     && w.TriggerSource == triggerSource
                     && w.AvailableAt <= now
                     && (w.Status == WorkItemStatus.Pending
                         || (w.Status == WorkItemStatus.Claimed && w.ClaimExpiresAt != null && w.ClaimExpiresAt <= now)))
            .ToListAsync(ct);

        var integrationIds = claimable.Select(w => w.IntegrationId).Distinct().ToList();

        var runningIntegrations = await db.ExecutionRecords
            .Where(e => e.TenantId == tenantId
                     && integrationIds.Contains(e.IntegrationId)
                     && e.Status == ExecutionStatus.Running)
            .Select(e => e.IntegrationId)
            .ToHashSetAsync(ct);

        var claimed = new List<ClaimedWork>();
        var claimExpiresAt = now.Add(claimDuration);

        foreach (var workItem in claimable)
        {
            if (workItem.Integration.Status != IntegrationStatus.Enabled)
                continue;

            if (!TagSet.IsSatisfiedBy(workItem.Integration.RequiredTags, agentTags))
                continue;

            if (runningIntegrations.Contains(workItem.IntegrationId))
                continue;

            if (workItem.HasActiveClaim(now) && workItem.ClaimOwner != claimOwner)
                continue;

            workItem.Status = WorkItemStatus.Claimed;
            workItem.ClaimOwner = claimOwner;
            workItem.ClaimExpiresAt = claimExpiresAt;
            workItem.UpdatedAt = now;

            claimed.Add(new ClaimedWork(workItem.Integration, workItem));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return claimed;
    }
}
