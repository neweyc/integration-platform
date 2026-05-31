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
}
