using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Secrets;

public class SecretRepository(AppDbContext db)
    : ISecretRepository, ISecretReadRepository, ISecretDeleteRepository
{
    public Task<Secret?> FindAsync(Guid tenantId, string environment, string key, CancellationToken ct = default) =>
        db.Secrets.FirstOrDefaultAsync(
            s => s.TenantId == tenantId && s.Environment == environment && s.Key == key, ct);

    public async Task<Secret> CreateAsync(Secret secret, CancellationToken ct = default)
    {
        db.Secrets.Add(secret);
        await db.SaveChangesAsync(ct);
        return secret;
    }

    public async Task<Secret> UpdateAsync(Secret secret, CancellationToken ct = default)
    {
        db.Secrets.Update(secret);
        await db.SaveChangesAsync(ct);
        return secret;
    }

    public async Task<bool> DeleteAsync(Guid tenantId, string environment, string key, CancellationToken ct = default)
    {
        var secret = await FindAsync(tenantId, environment, key, ct);

        if (secret is null)
            return false;

        db.Secrets.Remove(secret);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<Secret>> ListAsync(Guid tenantId, string environment, CancellationToken ct = default) =>
        await db.Secrets
            .Where(s => s.TenantId == tenantId && s.Environment == environment)
            .OrderBy(s => s.Key)
            .ToListAsync(ct);
}
