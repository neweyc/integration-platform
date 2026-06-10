using ControlPlane.Features.Auth;
using Shared.Domain;

namespace ControlPlane.Infrastructure;

// The access token (a JWT) and the refresh token issued alongside it. The access token is short
// lived; the refresh token is exchanged at /api/auth/refresh for a new pair.
public record AuthTokens(string AccessToken, string RefreshToken, DateTime RefreshTokenExpiresAt);

// Issues a fresh access + refresh token pair for a user. Used by every flow that signs a user in:
// login, first-run setup, and invitation acceptance.
public interface IAuthTokenIssuer
{
    Task<AuthTokens> IssueAsync(User user, CancellationToken ct = default);
}

public class AuthTokenIssuer(
    IJwtTokenService jwtTokenService,
    IRefreshTokenService refreshTokenService,
    IRefreshTokenRepository refreshTokenRepository,
    IConfiguration configuration) : IAuthTokenIssuer
{
    public async Task<AuthTokens> IssueAsync(User user, CancellationToken ct = default)
    {
        var accessToken = jwtTokenService.GenerateToken(user);

        var plaintext = refreshTokenService.Generate();
        var expiryDays = int.Parse(configuration["Jwt:RefreshTokenExpiryDays"] ?? "30");
        var expiresAt = DateTime.UtcNow.AddDays(expiryDays);

        await refreshTokenRepository.CreateAsync(new RefreshToken
        {
            UserId = user.Id,
            TenantId = user.TenantId,
            TokenHash = refreshTokenService.Hash(plaintext),
            ExpiresAt = expiresAt
        }, ct);

        return new AuthTokens(accessToken, plaintext, expiresAt);
    }
}
