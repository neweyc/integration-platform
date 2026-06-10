using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Billing;

public interface IBillingRepository
{
    Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<Tenant?> FindByStripeCustomerIdAsync(string customerId, CancellationToken ct = default);
    Task UpdateAsync(Tenant tenant, CancellationToken ct = default);
}

public class BillingRepository(AppDbContext db) : IBillingRepository
{
    public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);

    public Task<Tenant?> FindByStripeCustomerIdAsync(string customerId, CancellationToken ct = default) =>
        db.Tenants.FirstOrDefaultAsync(t => t.StripeCustomerId == customerId, ct);

    public async Task UpdateAsync(Tenant tenant, CancellationToken ct = default)
    {
        db.Tenants.Update(tenant);
        await db.SaveChangesAsync(ct);
    }
}
