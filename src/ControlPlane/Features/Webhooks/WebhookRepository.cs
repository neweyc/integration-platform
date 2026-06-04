using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared.Domain;

namespace ControlPlane.Features.Webhooks;

public class WebhookRepository(AppDbContext db) : IWebhookRepository
{
    public async Task<(Tenant Tenant, Integration Integration)?> FindAsync(
        string tenantSlug, string integrationSlug, CancellationToken ct = default)
    {
        var result = await db.Integrations
            .AsNoTracking()
            .Include(i => i.Tenant)
            .Where(i => i.Tenant.Slug == tenantSlug && i.Slug == integrationSlug)
            .Select(i => new { i.Tenant, Integration = i })
            .FirstOrDefaultAsync(ct);

        return result is null ? null : (result.Tenant, result.Integration);
    }

    public Task<bool> DeliveryExistsAsync(Guid tenantId, Guid integrationId, string deliveryId, CancellationToken ct = default) =>
        db.WorkItems.AnyAsync(
            w => w.TenantId == tenantId && w.IntegrationId == integrationId && w.DeliveryId == deliveryId, ct);

    public async Task<WorkItem?> CreateWorkItemAsync(WorkItem workItem, CancellationToken ct = default)
    {
        db.WorkItems.Add(workItem);
        try
        {
            await db.SaveChangesAsync(ct);
            return workItem;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Unique (TenantId, IntegrationId, DeliveryId) violation; a concurrent duplicate delivery won the race.
            db.Entry(workItem).State = EntityState.Detached;
            return null;
        }
    }

    public async Task RecordDeliveryAsync(WebhookDelivery delivery, CancellationToken ct = default)
    {
        db.WebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync(ct);
    }
}
