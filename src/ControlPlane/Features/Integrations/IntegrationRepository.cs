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

    public Task<int> CountAsync(Guid tenantId, CancellationToken ct = default) =>
        db.Integrations.CountAsync(i => i.TenantId == tenantId, ct);

    public async Task<HashSet<string>> ExistingSlugsAsync(Guid tenantId, IReadOnlyCollection<string> slugs, CancellationToken ct = default)
    {
        if (slugs.Count == 0)
            return new HashSet<string>();

        var existing = await db.Integrations
            .Where(i => i.TenantId == tenantId && slugs.Contains(i.Slug))
            .Select(i => i.Slug)
            .ToListAsync(ct);

        return existing.ToHashSet();
    }

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
        // Package upload is code-driven: record new declared defaults and preserve operator overrides.
        var tagsOverridden = ReconcileRequiredTags(existing, integration.DeclaredRequiredTags);
        var triggerResults = ReplaceTriggers(existing, triggers, TriggerReconcileSource.Code);

        await db.SaveChangesAsync(ct);
        return new IntegrationUpsertResult(existing, Created: false, triggerResults, tagsOverridden);
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
        // Operator-driven update: active values are taken as-is and become overrides when they
        // diverge from the code-declared defaults.
        ReplaceTriggers(integration, triggers, TriggerReconcileSource.Operator);
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
        IReadOnlyList<IntegrationTrigger> triggers,
        TriggerReconcileSource source)
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
                ReconcileRuntimeValues(existing, desired, source);
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
                    webhookSecretPreserved,
                    CronOverridden: IsCronOverridden(existing),
                    EnabledOverridden: existing.Enabled != existing.DeclaredEnabled));
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

    // Reconciles the active Enabled/CronExpression against the code-declared defaults.
    //
    // Code-driven (package upload): the new code values become the declared defaults. The active value
    // follows code only when it was not already an operator override; an existing override is preserved
    // and surfaces as drift (active != declared).
    //
    // Operator-driven (UI/API): the active values are taken as-is and the declared defaults are left
    // untouched, so an operator value that diverges from the declared default becomes an override.
    private static void ReconcileRuntimeValues(
        IntegrationTrigger existing,
        IntegrationTrigger desired,
        TriggerReconcileSource source)
    {
        if (source == TriggerReconcileSource.Operator)
        {
            existing.Enabled = desired.Enabled;
            existing.CronExpression = desired.CronExpression;
            // Like the webhook secret, a message subject is not edited through the operator surface
            // today, so a client that doesn't round-trip it must not erase the subscription. Only
            // overwrite when the operator actually supplied a subject (a Queue trigger with an empty
            // subject would match nothing, so preserving the existing value is always the safe choice).
            if (!string.IsNullOrWhiteSpace(desired.Subject))
                existing.Subject = desired.Subject;
            return;
        }

        var cronOverridden = IsCronOverridden(existing);
        var subjectOverridden = IsSubjectOverridden(existing);
        var enabledOverridden = existing.Enabled != existing.DeclaredEnabled;

        existing.DeclaredCronExpression = desired.CronExpression;
        existing.DeclaredSubject = desired.Subject;
        existing.DeclaredEnabled = desired.Enabled;

        if (!cronOverridden)
            existing.CronExpression = desired.CronExpression;

        if (!subjectOverridden)
            existing.Subject = desired.Subject;

        if (!enabledOverridden)
            existing.Enabled = desired.Enabled;
    }

    private static bool IsCronOverridden(IntegrationTrigger trigger) =>
        !string.Equals(trigger.CronExpression, trigger.DeclaredCronExpression, StringComparison.Ordinal);

    private static bool IsSubjectOverridden(IntegrationTrigger trigger) =>
        !string.Equals(trigger.Subject, trigger.DeclaredSubject, StringComparison.Ordinal);

    // Integration-level analog of ReconcileRuntimeValues for the required-tags set on a code-driven
    // (package) update: record the new code-declared tags, and let the active tags follow code only
    // when an operator hasn't already overridden them. Returns whether an override is in effect after
    // reconciliation (active != declared), i.e. drift to report.
    private static bool ReconcileRequiredTags(Integration existing, string[] declaredFromCode)
    {
        var wasOverridden = !TagSet.Equal(existing.RequiredTags, existing.DeclaredRequiredTags);

        existing.DeclaredRequiredTags = declaredFromCode;
        if (!wasOverridden)
            existing.RequiredTags = declaredFromCode;

        return !TagSet.Equal(existing.RequiredTags, existing.DeclaredRequiredTags);
    }
}
