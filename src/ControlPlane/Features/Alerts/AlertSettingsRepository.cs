using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Alerts;

// Read access used on the hot path (the dispatcher resolving where an alert goes). No tracking.
public interface IAlertSettingsReadRepository
{
    Task<TenantAlertSettings?> GetTenantSettingsAsync(Guid tenantId, CancellationToken ct = default);
    Task<IntegrationAlertSettings?> GetIntegrationSettingsAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default);
    Task<bool> IntegrationExistsAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default);
}

// Write access used by the configuration command handlers. Tracked fetches so a handler can mutate in
// place and preserve unchanged encrypted fields.
public interface IAlertSettingsWriteRepository
{
    Task<TenantAlertSettings?> FindTenantSettingsAsync(Guid tenantId, CancellationToken ct = default);
    Task AddTenantSettingsAsync(TenantAlertSettings settings, CancellationToken ct = default);
    Task<IntegrationAlertSettings?> FindIntegrationSettingsAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default);
    Task AddIntegrationSettingsAsync(IntegrationAlertSettings settings, CancellationToken ct = default);
    Task<bool> IntegrationExistsAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}

public class AlertSettingsRepository(AppDbContext db)
    : IAlertSettingsReadRepository, IAlertSettingsWriteRepository
{
    public Task<TenantAlertSettings?> GetTenantSettingsAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TenantAlertSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

    public Task<IntegrationAlertSettings?> GetIntegrationSettingsAsync(
        Guid tenantId, Guid integrationId, CancellationToken ct = default) =>
        db.IntegrationAlertSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.IntegrationId == integrationId, ct);

    public Task<TenantAlertSettings?> FindTenantSettingsAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TenantAlertSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

    public async Task AddTenantSettingsAsync(TenantAlertSettings settings, CancellationToken ct = default) =>
        await db.TenantAlertSettings.AddAsync(settings, ct);

    public Task<IntegrationAlertSettings?> FindIntegrationSettingsAsync(
        Guid tenantId, Guid integrationId, CancellationToken ct = default) =>
        db.IntegrationAlertSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.IntegrationId == integrationId, ct);

    public async Task AddIntegrationSettingsAsync(IntegrationAlertSettings settings, CancellationToken ct = default) =>
        await db.IntegrationAlertSettings.AddAsync(settings, ct);

    public Task<bool> IntegrationExistsAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default) =>
        db.Integrations.AnyAsync(i => i.TenantId == tenantId && i.Id == integrationId, ct);

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
