using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public class PollRepository(AppDbContext db) : IPollRepository
{
    public async Task<IReadOnlyList<Integration>> ListEnabledAsync(
        Guid tenantId,
        string environment,
        CancellationToken ct = default)
    {
        return await db.Integrations
            .Where(i => i.TenantId == tenantId
                     && i.Environment == environment
                     && i.Status == IntegrationStatus.Enabled)
            .ToListAsync(ct);
    }
}
