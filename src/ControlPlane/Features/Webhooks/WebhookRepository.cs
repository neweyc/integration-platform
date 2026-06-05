using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Webhooks;

public class WebhookRepository(AppDbContext db) : IWebhookRepository
{
    public async Task<(Tenant Tenant, Integration Integration, IntegrationTrigger Trigger)?> FindAsync(
        string tenantSlug, string integrationSlug, string triggerSlug, CancellationToken ct = default)
    {
        var result = await db.IntegrationTriggers
            .AsNoTracking()
            .Include(t => t.Integration)
            .ThenInclude(i => i.Tenant)
            .Where(t => t.Type == TriggerType.Webhook
                     && t.Integration.Tenant.Slug == tenantSlug
                     && t.Integration.Slug == integrationSlug
                     && t.Slug == triggerSlug)
            .Select(t => new { t.Integration.Tenant, t.Integration, Trigger = t })
            .FirstOrDefaultAsync(ct);

        return result is null ? null : (result.Tenant, result.Integration, result.Trigger);
    }

    public Task<bool> DeliveryExistsAsync(
        Guid tenantId,
        Guid integrationId,
        Guid integrationTriggerId,
        string deliveryId,
        CancellationToken ct = default) =>
        db.WorkItems.AnyAsync(
            w => w.TenantId == tenantId
                 && w.IntegrationId == integrationId
                 && w.IntegrationTriggerId == integrationTriggerId
                 && w.DeliveryId == deliveryId, ct);

    public async Task RecordDeliveryAsync(WebhookDelivery delivery, CancellationToken ct = default)
    {
        db.WebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync(ct);
    }
}
