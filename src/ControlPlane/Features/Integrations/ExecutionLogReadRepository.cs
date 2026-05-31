using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

public class ExecutionLogReadRepository(AppDbContext db) : IExecutionLogReadRepository
{
    public Task<bool> ExecutionBelongsToIntegrationAsync(
        Guid tenantId,
        Guid integrationId,
        Guid executionId,
        CancellationToken ct = default)
    {
        return db.ExecutionRecords.AnyAsync(
            e => e.TenantId == tenantId && e.IntegrationId == integrationId && e.Id == executionId,
            ct);
    }

    public async Task<IReadOnlyList<ExecutionLog>> ListForExecutionAsync(
        Guid tenantId,
        Guid executionId,
        CancellationToken ct = default)
    {
        return await db.ExecutionLogs
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.ExecutionRecordId == executionId)
            .OrderBy(l => l.Timestamp)
            .ToListAsync(ct);
    }
}
