using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public class AgentTokenRepository(AppDbContext db)
    : IAgentTokenRepository,
      IAgentTokenReadRepository,
      IAgentTokenDeleteRepository,
      IAgentTokenLookupRepository
{
    public async Task<AgentToken> CreateAsync(AgentToken token, CancellationToken ct = default)
    {
        db.AgentTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<IReadOnlyList<AgentToken>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await db.AgentTokens
            .Where(t => t.TenantId == tenantId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<AgentToken?> FindAsync(Guid tenantId, Guid tokenId, CancellationToken ct = default)
    {
        return await db.AgentTokens
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == tokenId, ct);
    }

    public async Task DeleteAsync(AgentToken token, CancellationToken ct = default)
    {
        db.AgentTokens.Remove(token);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AgentToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        return await db.AgentTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
    }
}
