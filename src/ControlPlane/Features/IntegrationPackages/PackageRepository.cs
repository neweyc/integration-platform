using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.IntegrationPackages;

public class PackageRepository(AppDbContext db)
    : IPackageRepository, IPackageReadRepository, IPackageDeleteRepository, IPackageActivationRepository
{
    public Task<bool> VersionExistsAsync(
        Guid tenantId,
        string name,
        string version,
        CancellationToken ct = default)
    {
        return db.AssemblyPackages.AnyAsync(
            p => p.TenantId == tenantId && p.Name == name && p.Version == version,
            ct);
    }

    public async Task<AssemblyPackage> CreateAsync(AssemblyPackage package, CancellationToken ct = default)
    {
        db.AssemblyPackages.Add(package);
        await db.SaveChangesAsync(ct);
        return package;
    }

    public async Task<IReadOnlyList<AssemblyPackage>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await db.AssemblyPackages
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.Name)
            .ThenByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
    }

    public Task<AssemblyPackage?> GetAsync(Guid tenantId, Guid packageId, CancellationToken ct = default)
    {
        return db.AssemblyPackages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == packageId, ct);
    }

    public async Task<IReadOnlyList<string>> ListPinnedIntegrationNamesAsync(
        Guid tenantId, Guid packageId, CancellationToken ct = default)
    {
        return await db.Integrations
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.PackageId == packageId)
            .Select(i => i.Name)
            .ToListAsync(ct);
    }

    // Read-only: the package is only scanned for its class list, never modified, so no tracking.
    public async Task<AssemblyPackage?> GetPackageAsync(Guid tenantId, Guid packageId, CancellationToken ct = default) =>
        await db.AssemblyPackages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == packageId, ct);

    public async Task<IReadOnlyList<Integration>> ListIntegrationsForPackageNameAsync(
        Guid tenantId, string packageName, CancellationToken ct = default)
    {
        var packageIds = await db.AssemblyPackages
            .Where(p => p.TenantId == tenantId && p.Name == packageName)
            .Select(p => p.Id)
            .ToListAsync(ct);

        // Tracked (no AsNoTracking) so the handler's PackageId changes are persisted by SaveAsync.
        return await db.Integrations
            .Where(i => i.TenantId == tenantId && i.PackageId != null && packageIds.Contains(i.PackageId.Value))
            .ToListAsync(ct);
    }

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task<bool> DeleteAsync(Guid tenantId, Guid packageId, CancellationToken ct = default)
    {
        var package = await db.AssemblyPackages
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == packageId, ct);

        if (package is null)
            return false;

        db.AssemblyPackages.Remove(package);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
