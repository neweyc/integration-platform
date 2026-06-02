using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public interface IScheduleStateRepository
{
    Task<IntegrationScheduleState?> GetByIntegrationIdAsync(
        Guid tenantId, Guid integrationId, CancellationToken ct = default);
}

public class ScheduleStateRepository(AppDbContext db) : IScheduleStateRepository
{
    public Task<IntegrationScheduleState?> GetByIntegrationIdAsync(
        Guid tenantId, Guid integrationId, CancellationToken ct = default)
    {
        return db.IntegrationScheduleStates
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.IntegrationId == integrationId, ct);
    }
}
