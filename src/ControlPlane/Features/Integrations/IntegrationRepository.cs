using ControlPlane.Features.AgentTokens;
using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

public class IntegrationRepository(AppDbContext db)
    : IIntegrationRepository, IIntegrationReadRepository, IIntegrationUpdateRepository, IIntegrationDeleteRepository, IIntegrationValidationRepository
{
    public Task<bool> SlugExistsAsync(Guid tenantId, string slug, CancellationToken ct = default) =>
        db.Integrations.AnyAsync(i => i.TenantId == tenantId && i.Slug == slug, ct);

    public Task<bool> PackageExistsAsync(Guid tenantId, Guid packageId, CancellationToken ct = default) =>
        db.AssemblyPackages.AnyAsync(p => p.TenantId == tenantId && p.Id == packageId, ct);

    public async Task<string?> GetTenantSlugAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        return tenant?.Slug;
    }

    public async Task<Integration> CreateAsync(Integration integration, CancellationToken ct = default)
    {
        db.Integrations.Add(integration);
        await db.SaveChangesAsync(ct);
        return integration;
    }

    public async Task<Integration> UpsertBySlugAsync(Integration integration, CancellationToken ct = default)
    {
        var existing = await db.Integrations
            .FirstOrDefaultAsync(i => i.TenantId == integration.TenantId && i.Slug == integration.Slug, ct);

        if (existing == null)
        {
            db.Integrations.Add(integration);
            await db.SaveChangesAsync(ct);
            return integration;
        }

        existing.Name = integration.Name;
        existing.Description = integration.Description;
        existing.Environment = integration.Environment;
        existing.TriggerType = integration.TriggerType;
        existing.CronExpression = integration.CronExpression;
        existing.ClassName = integration.ClassName;
        existing.TimeoutSeconds = integration.TimeoutSeconds;
        existing.RetryMaxAttempts = integration.RetryMaxAttempts;
        existing.RetryBackoffSeconds = integration.RetryBackoffSeconds;
        existing.PackageId = integration.PackageId;
        existing.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return existing;
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
