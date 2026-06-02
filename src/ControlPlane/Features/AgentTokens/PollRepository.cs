using System.Data;
using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public class PollRepository(AppDbContext db) : IPollRepository
{
    public async Task<IReadOnlyList<ClaimedWork>> ClaimDueScheduledAsync(
        Guid tenantId,
        string environment,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var integrations = await db.Integrations
            .Where(i => i.TenantId == tenantId
                     && i.Environment == environment
                     && i.Status == IntegrationStatus.Enabled
                     && i.TriggerType == TriggerType.Scheduled
                     && i.CronExpression != null)
            .ToListAsync(ct);

        var integrationIds = integrations.Select(i => i.Id).ToList();

        var states = await db.IntegrationScheduleStates
            .Where(s => s.TenantId == tenantId && integrationIds.Contains(s.IntegrationId))
            .ToDictionaryAsync(s => s.IntegrationId, ct);

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
            .Where(w => w.TenantId == tenantId
                     && w.Environment == environment
                     && integrationIds.Contains(w.IntegrationId)
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

            if (runningIntegrations.Contains(workItem.IntegrationId))
                continue;

            workItem.ClaimOwner = claimOwner;
            workItem.ClaimExpiresAt = claimExpiresAt;
            workItem.UpdatedAt = now;

            claimed.Add(new ClaimedWork(workItem.Integration, workItem));
        }

        var reclaimedIntegrationIds = claimed.Select(c => c.Integration.Id).ToHashSet();

        foreach (var integration in integrations)
        {
            if (reclaimedIntegrationIds.Contains(integration.Id))
                continue;

            if (activeWorkItemIntegrations.Contains(integration.Id))
                continue;

            if (runningIntegrations.Contains(integration.Id))
                continue;

            states.TryGetValue(integration.Id, out var state);

            try
            {
                var decision = ScheduleStateCalculator.Evaluate(integration, state, now);

                if (state is null)
                {
                    state = new IntegrationScheduleState
                    {
                        TenantId = tenantId,
                        IntegrationId = integration.Id
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
                        Environment = environment,
                        TriggerSource = TriggerSource.Scheduled,
                        Status = WorkItemStatus.Claimed,
                        AvailableAt = now,
                        ClaimOwner = claimOwner,
                        ClaimExpiresAt = claimExpiresAt
                    };
                    db.WorkItems.Add(workItem);

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
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        // Find pending work items or those with an expired claim
        var claimable = await db.WorkItems
            .Include(w => w.Integration)
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
}
