using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Infrastructure;

public interface IQuotaService
{
    Task<bool> HasAvailableExecutionsAsync(Guid tenantId, CancellationToken ct = default);
    Task<int> GetCurrentMonthlyExecutionCountAsync(Guid tenantId, CancellationToken ct = default);
}

public class QuotaService(AppDbContext db) : IQuotaService
{
    public async Task<bool> HasAvailableExecutionsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.FindAsync([tenantId], ct);
        if (tenant is null) return false;

        var count = await GetCurrentMonthlyExecutionCountAsync(tenantId, ct);
        return count < tenant.MaxExecutionsPerMonth;
    }

    public Task<int> GetCurrentMonthlyExecutionCountAsync(Guid tenantId, CancellationToken ct = default)
    {
        var firstDayOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return db.ExecutionRecords.CountAsync(e => e.TenantId == tenantId && e.StartedAt >= firstDayOfMonth, ct);
    }
}
