using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public class ExecutionLogRepository(AppDbContext db) : IExecutionLogRepository
{
    public Task<bool> ExecutionExistsAsync(
        Guid tenantId,
        string environment,
        Guid executionId,
        CancellationToken ct = default)
    {
        return db.ExecutionRecords.AnyAsync(
            e => e.TenantId == tenantId && e.Environment == environment && e.Id == executionId,
            ct);
    }

    public async Task<ExecutionLog> CreateAsync(ExecutionLog log, CancellationToken ct = default)
    {
        db.ExecutionLogs.Add(log);
        await db.SaveChangesAsync(ct);
        return log;
    }
}
