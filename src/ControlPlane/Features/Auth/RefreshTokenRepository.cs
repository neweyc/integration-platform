using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Auth;

public interface IRefreshTokenRepository
{
    Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken ct = default);
    Task<RefreshToken?> FindByHashAsync(string hash, CancellationToken ct = default);
    Task UpdateAsync(RefreshToken token, CancellationToken ct = default);
    Task RevokeAllForUserAsync(Guid userId, DateTime revokedAt, CancellationToken ct = default);
}

public class RefreshTokenRepository(AppDbContext db) : IRefreshTokenRepository
{
    public async Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken ct = default)
    {
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return token;
    }

    // Includes the owning user so callers can re-issue tokens without a second lookup.
    public Task<RefreshToken?> FindByHashAsync(string hash, CancellationToken ct = default) =>
        db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

    public async Task UpdateAsync(RefreshToken token, CancellationToken ct = default)
    {
        db.RefreshTokens.Update(token);
        await db.SaveChangesAsync(ct);
    }

    // Revokes every still-active token for a user — used on password reset and on detecting reuse
    // of an already-revoked token (a sign the token may have leaked).
    public async Task RevokeAllForUserAsync(Guid userId, DateTime revokedAt, CancellationToken ct = default)
    {
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedAt), ct);
    }
}
