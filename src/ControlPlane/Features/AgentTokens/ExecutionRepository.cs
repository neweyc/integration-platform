using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public class ExecutionRepository(AppDbContext db) : IExecutionRepository
{
    public async Task<ExecutionRecord> CreateAsync(ExecutionRecord record, CancellationToken ct = default)
    {
        db.ExecutionRecords.Add(record);
        await db.SaveChangesAsync(ct);
        return record;
    }

    public async Task<ExecutionRecord?> FindAsync(Guid tenantId, Guid executionId, CancellationToken ct = default)
    {
        return await db.ExecutionRecords
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == executionId, ct);
    }

    public async Task UpdateAsync(ExecutionRecord record, CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> HasRunningExecutionAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default)
    {
        return await db.ExecutionRecords
            .AnyAsync(e => e.TenantId == tenantId
                        && e.IntegrationId == integrationId
                        && e.Status == ExecutionStatus.Running, ct);
    }
}

public class ManualRunRequestRepository(AppDbContext db) : IManualRunRequestRepository
{
    public async Task<ManualRunRequest?> GetByIdAsync(Guid tenantId, Guid requestId, CancellationToken ct = default)
    {
        return await db.ManualRunRequests
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == requestId, ct);
    }

    public async Task MarkStartedAsync(Guid requestId, Guid executionRecordId, CancellationToken ct = default)
    {
        var request = await db.ManualRunRequests.FindAsync([requestId], ct);
        if (request is not null)
        {
            request.Status = ManualRunStatus.Started;
            request.ExecutionRecordId = executionRecordId;
            request.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }
}
