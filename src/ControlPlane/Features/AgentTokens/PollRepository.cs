using System.Data;
using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public class PollRepository(AppDbContext db) : IPollRepository
{
    public async Task<IReadOnlyList<Integration>> ClaimDueScheduledAsync(
        Guid tenantId,
        string environment,
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

        var due = new List<Integration>();

        foreach (var integration in integrations)
        {
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

                state.LastDispatchedAt = decision.LastDispatchedAt;
                state.NextRunAt = decision.NextRunAt;
                state.UpdatedAt = now;

                if (decision.IsDue)
                    due.Add(integration);
            }
            catch
            {
                // Invalid cron expressions should be rejected by integration validation.
                // If legacy data is invalid, skip it without blocking the whole poll cycle.
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return due;
    }
}
