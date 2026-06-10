using System.Security.Cryptography;
using System.Text;
using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Auth;

// Generates and hashes password-reset tokens. The plaintext is emailed to the user; only its hash
// is persisted, so a leaked database never reveals a usable reset link.
public static class PasswordResetTokens
{
    public static string Generate()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public interface IPasswordResetRepository
{
    Task<User?> GetUserByEmailAsync(string email, CancellationToken ct = default);
    Task AddAsync(PasswordResetToken token, CancellationToken ct = default);
    Task<PasswordResetToken?> FindUnusedByHashAsync(string hash, CancellationToken ct = default);
    Task ConsumeAndSetPasswordAsync(PasswordResetToken token, string newPasswordHash, CancellationToken ct = default);
}

public class PasswordResetRepository(AppDbContext db) : IPasswordResetRepository
{
    public Task<User?> GetUserByEmailAsync(string email, CancellationToken ct = default) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public async Task AddAsync(PasswordResetToken token, CancellationToken ct = default)
    {
        db.PasswordResetTokens.Add(token);
        await db.SaveChangesAsync(ct);
    }

    public Task<PasswordResetToken?> FindUnusedByHashAsync(string hash, CancellationToken ct = default) =>
        db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.UsedAt == null, ct);

    // Marks the token used and updates the owning user's password in a single transaction so a token
    // can never reset a password twice.
    public async Task ConsumeAndSetPasswordAsync(PasswordResetToken token, string newPasswordHash, CancellationToken ct = default)
    {
        token.UsedAt = DateTime.UtcNow;
        token.User.PasswordHash = newPasswordHash;
        await db.SaveChangesAsync(ct);
    }
}
