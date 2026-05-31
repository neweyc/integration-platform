using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

public class ExecutionHistoryRepository(AppDbContext db) : IExecutionHistoryRepository
{
    public async Task<ExecutionRecord?> GetLatestForIntegrationAsync(
        Guid tenantId,
        Guid integrationId,
        CancellationToken ct = default)
    {
        return await db.ExecutionRecords
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.IntegrationId == integrationId)
            .OrderByDescending(e => e.StartedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ExecutionRecord>> ListForIntegrationAsync(
        Guid tenantId,
        Guid integrationId,
        int limit,
        CancellationToken ct = default)
    {
        return await db.ExecutionRecords
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.IntegrationId == integrationId)
            .OrderByDescending(e => e.StartedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, ExecutionRecord>> GetLatestForIntegrationsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> integrationIds,
        CancellationToken ct = default)
    {
        if (integrationIds.Count == 0)
            return [];

        return await db.ExecutionRecords
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && integrationIds.Contains(e.IntegrationId))
            .GroupBy(e => e.IntegrationId)
            .Select(g => g.OrderByDescending(e => e.StartedAt).First())
            .ToDictionaryAsync(e => e.IntegrationId, ct);
    }
}
