using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Alerts;

public class UnroutableAlertRepository(AppDbContext db) : IUnroutableAlertRepository
{
    // Tracked so the monitor can update UnroutableAlertedAt. Filtered to enabled, tag-gated
    // integrations across all tenants — the only ones that can be capability-unroutable.
    public async Task<IReadOnlyList<Integration>> ListEnabledTagGatedIntegrationsAsync(CancellationToken ct = default) =>
        await db.Integrations
            .Where(i => i.Status == IntegrationStatus.Enabled && i.RequiredTags.Length > 0)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AgentHeartbeat>> ListLiveAgentsAsync(DateTime liveSince, CancellationToken ct = default) =>
        await db.AgentHeartbeats
            .AsNoTracking()
            .Where(h => h.LastSeenAt > liveSince)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
