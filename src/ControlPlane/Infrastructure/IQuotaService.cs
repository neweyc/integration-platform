using ControlPlane.Features.Billing;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Infrastructure;

public interface IQuotaService
{
    Task<bool> HasAvailableExecutionsAsync(Guid tenantId, CancellationToken ct = default);
    Task<int> GetCurrentMonthlyExecutionCountAsync(Guid tenantId, CancellationToken ct = default);
}

public class QuotaService(AppDbContext db, StripeOptions stripeOptions) : IQuotaService
{
    public async Task<bool> HasAvailableExecutionsAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Execution metering is a cloud-only cost control. Self-hosted (Community/Commercial) is gated on
        // estate size — integrations and environments — not executions, so an on-prem deployment with no
        // billing configured runs unmetered. See docs/licensing.md.
        if (!stripeOptions.IsConfigured)
            return true;

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
