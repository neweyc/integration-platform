using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

public class IntegrationRepository(AppDbContext db)
    : IIntegrationRepository, IIntegrationReadRepository, IIntegrationUpdateRepository, IIntegrationDeleteRepository
{
    public Task<bool> SlugExistsAsync(Guid tenantId, string slug, CancellationToken ct = default) =>
        db.Integrations.AnyAsync(i => i.TenantId == tenantId && i.Slug == slug, ct);

    public async Task<Integration> CreateAsync(Integration integration, CancellationToken ct = default)
    {
        db.Integrations.Add(integration);
        await db.SaveChangesAsync(ct);
        return integration;
    }

    public Task<Integration?> GetByIdAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default) =>
        db.Integrations.FirstOrDefaultAsync(
            i => i.TenantId == tenantId && i.Id == integrationId, ct);

    public async Task<IReadOnlyList<Integration>> ListAsync(Guid tenantId, string? environment, CancellationToken ct = default)
    {
        var query = db.Integrations.Where(i => i.TenantId == tenantId);

        // Filter by environment if provided
        if (!string.IsNullOrWhiteSpace(environment))
            query = query.Where(i => i.Environment == environment);

        return await query.OrderBy(i => i.Name).ToListAsync(ct);
    }

    public async Task<Integration> UpdateAsync(Integration integration, CancellationToken ct = default)
    {
        db.Integrations.Update(integration);
        await db.SaveChangesAsync(ct);
        return integration;
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default)
    {
        var integration = await GetByIdAsync(tenantId, integrationId, ct);

        if (integration is null)
            return false;

        db.Integrations.Remove(integration);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
