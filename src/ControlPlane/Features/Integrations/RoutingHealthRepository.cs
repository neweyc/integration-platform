using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

public class RoutingHealthRepository(AppDbContext db) : IRoutingHealthRepository
{
    public async Task<IReadOnlyList<Integration>> ListEnabledIntegrationsAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.Integrations
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.Status == IntegrationStatus.Enabled)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AgentHeartbeat>> ListLiveAgentsAsync(Guid tenantId, DateTime liveSince, CancellationToken ct = default) =>
        await db.AgentHeartbeats
            .AsNoTracking()
            .Where(h => h.TenantId == tenantId && h.LastSeenAt > liveSince)
            .ToListAsync(ct);
}
