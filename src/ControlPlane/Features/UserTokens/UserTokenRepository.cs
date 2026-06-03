using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.UserTokens;

public interface IUserTokenRepository
{
    Task<UserToken> CreateAsync(UserToken token, CancellationToken ct = default);
    Task<IReadOnlyList<UserToken>> ListAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
    Task<UserToken?> FindByHashAsync(string hash, CancellationToken ct = default);
    Task DeleteAsync(Guid tenantId, Guid tokenId, CancellationToken ct = default);
}

public class UserTokenRepository(AppDbContext db) : IUserTokenRepository
{
    public async Task<UserToken> CreateAsync(UserToken token, CancellationToken ct = default)
    {
        db.UserTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<IReadOnlyList<UserToken>> ListAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        return await db.UserTokens
            .Where(t => t.TenantId == tenantId && t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public Task<UserToken?> FindByHashAsync(string hash, CancellationToken ct = default)
    {
        return db.UserTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
    }

    public async Task DeleteAsync(Guid tenantId, Guid tokenId, CancellationToken ct = default)
    {
        var token = await db.UserTokens
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == tokenId, ct);

        if (token != null)
        {
            db.UserTokens.Remove(token);
            await db.SaveChangesAsync(ct);
        }
    }
}
