using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Setup;

public class SetupRepository(AppDbContext db) : ISetupRepository
{
    public Task<bool> AnyTenantExistsAsync(CancellationToken ct = default) =>
        db.Tenants.AnyAsync(ct);

    public async Task<Tenant> CreateTenantAsync(Tenant tenant, CancellationToken ct = default)
    {
        db.Tenants.Add(tenant);
        db.Environments.Add(TenantEnvironments.Default(tenant.Id));
        await db.SaveChangesAsync(ct);
        return tenant;
    }

    public async Task<User> CreateUserAsync(User user, CancellationToken ct = default)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }
}
