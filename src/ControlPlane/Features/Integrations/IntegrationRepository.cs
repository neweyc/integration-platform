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

    public async Task<Integration> CreateAsync(
        Integration integration,
        IReadOnlyList<IntegrationTrigger> triggers,
        CancellationToken ct = default)
    {
        foreach (var trigger in triggers)
            integration.Triggers.Add(trigger);

        db.Integrations.Add(integration);
        await db.SaveChangesAsync(ct);
        return integration;
    }

    public async Task<IntegrationUpsertResult> UpsertBySlugAsync(
        Integration integration,
        IReadOnlyList<IntegrationTrigger> triggers,
        CancellationToken ct = default)
    {
        var existing = await db.Integrations
            .Include(i => i.Triggers)
            .FirstOrDefaultAsync(i => i.TenantId == integration.TenantId && i.Slug == integration.Slug, ct);

        if (existing == null)
        {
            foreach (var trigger in triggers)
                integration.Triggers.Add(trigger);

            db.Integrations.Add(integration);
            await db.SaveChangesAsync(ct);
            return new IntegrationUpsertResult(
                integration,
                Created: true,
                integration.Triggers.Select(t => new IntegrationTriggerUpsertResult(
                    t,
                    Created: true,
                    WebhookSecretPreserved: false)).ToList());
        }

        existing.Name = integration.Name;
        existing.Description = integration.Description;
        existing.Environment = integration.Environment;
        existing.ClassName = integration.ClassName;
        existing.TimeoutSeconds = integration.TimeoutSeconds;
        existing.RetryMaxAttempts = integration.RetryMaxAttempts;
        existing.RetryBackoffSeconds = integration.RetryBackoffSeconds;
        existing.PackageId = integration.PackageId;
        existing.UpdatedAt = DateTime.UtcNow;
        var triggerResults = ReplaceTriggers(existing, triggers);

        await db.SaveChangesAsync(ct);
        return new IntegrationUpsertResult(existing, Created: false, triggerResults);
    }

    public Task<Integration?> GetByIdAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default) =>
        db.Integrations
            .Include(i => i.Triggers)
            .FirstOrDefaultAsync(
            i => i.TenantId == tenantId && i.Id == integrationId, ct);

    public async Task<IReadOnlyList<Integration>> ListAsync(Guid tenantId, string? environment, CancellationToken ct = default)
    {
        var query = db.Integrations.Where(i => i.TenantId == tenantId);

        // Filter by environment if provided
        if (!string.IsNullOrWhiteSpace(environment))
            query = query.Where(i => i.Environment == environment);

        return await query
            .Include(i => i.Triggers)
            .OrderBy(i => i.Name)
            .ToListAsync(ct);
    }

    public async Task<Integration> UpdateAsync(
        Integration integration,
        IReadOnlyList<IntegrationTrigger> triggers,
        CancellationToken ct = default)
    {
        ReplaceTriggers(integration, triggers);
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

    private static IReadOnlyList<IntegrationTriggerUpsertResult> ReplaceTriggers(
        Integration integration,
        IReadOnlyList<IntegrationTrigger> triggers)
    {
        var existingBySlug = integration.Triggers.ToDictionary(t => t.Slug, StringComparer.OrdinalIgnoreCase);
        var desiredSlugs = triggers.Select(t => t.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new List<IntegrationTriggerUpsertResult>();

        foreach (var existing in integration.Triggers.Where(t => !desiredSlugs.Contains(t.Slug)).ToList())
            integration.Triggers.Remove(existing);

        foreach (var desired in triggers)
        {
            if (existingBySlug.TryGetValue(desired.Slug, out var existing))
            {
                var webhookSecretPreserved = existing.Type == TriggerType.Webhook
                                             && desired.Type == TriggerType.Webhook
                                             && existing.EncryptedWebhookSecret is not null;
                existing.Name = desired.Name;
                existing.Type = desired.Type;
                existing.Enabled = desired.Enabled;
                existing.CronExpression = desired.CronExpression;
                if (webhookSecretPreserved)
                {
                    // Existing webhook secrets are operator-facing credentials; package upload must not rotate them.
                }
                else if (desired.EncryptedWebhookSecret is not null)
                {
                    existing.EncryptedWebhookSecret = desired.EncryptedWebhookSecret;
                }
                else if (desired.Type != TriggerType.Webhook)
                {
                    existing.EncryptedWebhookSecret = null;
                }
                existing.UpdatedAt = DateTime.UtcNow;
                results.Add(new IntegrationTriggerUpsertResult(
                    existing,
                    Created: false,
                    webhookSecretPreserved));
                continue;
            }

            integration.Triggers.Add(desired);
            results.Add(new IntegrationTriggerUpsertResult(
                desired,
                Created: true,
                WebhookSecretPreserved: false));
        }

        return results;
    }
}
