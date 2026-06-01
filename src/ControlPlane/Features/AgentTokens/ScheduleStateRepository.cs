using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public class ScheduleStateRepository(AppDbContext db) : IScheduleStateRepository
{
    public async Task<IntegrationScheduleState?> GetByIntegrationIdAsync(
        Guid tenantId,
        Guid integrationId,
        CancellationToken ct = default)
    {
        return await db.IntegrationScheduleStates
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.IntegrationId == integrationId, ct);
    }

    public async Task ClearLeaseAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default)
    {
        var state = await GetByIntegrationIdAsync(tenantId, integrationId, ct);
        if (state is not null)
        {
            state.LeaseOwnerId = null;
            state.LeaseExpiresAt = null;
            state.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }
}
