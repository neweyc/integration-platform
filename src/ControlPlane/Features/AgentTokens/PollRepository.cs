using System.Data;
using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public class PollRepository(AppDbContext db) : IPollRepository
{
    public async Task<IReadOnlyList<ClaimedIntegration>> ClaimDueScheduledAsync(
        Guid tenantId,
        string environment,
        Guid leaseOwnerId,
        TimeSpan leaseDuration,
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

        var claimed = new List<ClaimedIntegration>();

        foreach (var integration in integrations)
        {
            states.TryGetValue(integration.Id, out var state);

            try
            {
                // Skip if another agent holds an active lease (not expired, not ours)
                if (state is not null && state.HasActiveLease(now) && state.LeaseOwnerId != leaseOwnerId)
                    continue;

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
                    // Claim this integration with a lease
                    var leaseExpiresAt = now.Add(leaseDuration);
                    state.LastDispatchedAt = decision.LastDispatchedAt;
                    state.NextRunAt = decision.NextRunAt;
                    state.LeaseOwnerId = leaseOwnerId;
                    state.LeaseExpiresAt = leaseExpiresAt;
                    state.UpdatedAt = now;

                    claimed.Add(new ClaimedIntegration(integration, leaseExpiresAt));
                }
                else
                {
                    // Not due - just update schedule state without claiming
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

    public async Task<IReadOnlyList<ClaimedManualRun>> ClaimPendingManualRunsAsync(
        Guid tenantId,
        string environment,
        Guid agentTokenId,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        // Find pending or expired-claim manual run requests for this environment
        var claimableRequests = await db.ManualRunRequests
            .Include(r => r.Integration)
            .Where(r => r.TenantId == tenantId
                     && r.Environment == environment
                     && (r.Status == ManualRunStatus.Pending ||
                         (r.Status == ManualRunStatus.Claimed && r.ClaimExpiresAt != null && r.ClaimExpiresAt <= now)))
            .ToListAsync(ct);

        // Get integrations that are currently running to prevent overlap
        var integrationIds = claimableRequests.Select(r => r.IntegrationId).Distinct().ToList();
        var runningIntegrations = await db.ExecutionRecords
            .Where(e => e.TenantId == tenantId
                     && integrationIds.Contains(e.IntegrationId)
                     && e.Status == ExecutionStatus.Running)
            .Select(e => e.IntegrationId)
            .ToHashSetAsync(ct);

        var claimed = new List<ClaimedManualRun>();
        var claimExpiresAt = now.Add(claimDuration);

        foreach (var request in claimableRequests)
        {
            // Skip if the integration is not enabled
            if (request.Integration.Status != IntegrationStatus.Enabled)
                continue;

            // Skip if the integration is already running (prevents overlap)
            if (runningIntegrations.Contains(request.IntegrationId))
                continue;

            // Skip if another agent holds an active claim (not ours, not expired)
            if (request.HasActiveClaim(now) && request.ClaimedByAgentTokenId != agentTokenId)
                continue;

            // Claim the request
            request.Status = ManualRunStatus.Claimed;
            request.ClaimedByAgentTokenId = agentTokenId;
            request.ClaimedAt = now;
            request.ClaimExpiresAt = claimExpiresAt;
            request.UpdatedAt = now;

            claimed.Add(new ClaimedManualRun(request, request.Integration));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return claimed;
    }
}
