using ControlPlane.Infrastructure;

namespace ControlPlane.Features.Auth;

// Exchanges a refresh token for a new access + refresh token pair, rotating the presented token.
public record RefreshSessionCommand(string RefreshToken) : ICommand<AuthTokens>;

public class RefreshSessionHandler(
    IRefreshTokenRepository repository,
    IRefreshTokenService refreshTokenService,
    IAuthTokenIssuer issuer)
    : ICommandHandler<RefreshSessionCommand, AuthTokens>
{
    public async Task<AuthTokens> HandleAsync(RefreshSessionCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
            throw new UnauthorizedException("Refresh token is required.");

        var hash = refreshTokenService.Hash(command.RefreshToken.Trim());
        var existing = await repository.FindByHashAsync(hash, ct);

        if (existing is null)
            throw new UnauthorizedException("Invalid refresh token.");

        // Presenting a token that was already revoked means either a replayed logout or a leaked
        // token being reused after rotation. Treat it as compromise and revoke the whole chain.
        if (existing.RevokedAt is not null)
        {
            await repository.RevokeAllForUserAsync(existing.UserId, DateTime.UtcNow, ct);
            throw new UnauthorizedException("Refresh token has been revoked.");
        }

        if (existing.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedException("Refresh token has expired.");

        // Rotate: mint a new pair, then retire the presented token and record its successor.
        var tokens = await issuer.IssueAsync(existing.User, ct);
        existing.RevokedAt = DateTime.UtcNow;
        existing.ReplacedByTokenHash = refreshTokenService.Hash(tokens.RefreshToken);
        await repository.UpdateAsync(existing, ct);

        return tokens;
    }
}

// Revokes a refresh token so it can no longer be exchanged. Idempotent — an unknown or
// already-revoked token still reports success so logout never leaks token validity.
public record LogoutCommand(string RefreshToken) : ICommand<bool>;

public class LogoutHandler(
    IRefreshTokenRepository repository,
    IRefreshTokenService refreshTokenService)
    : ICommandHandler<LogoutCommand, bool>
{
    public async Task<bool> HandleAsync(LogoutCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
            return true;

        var hash = refreshTokenService.Hash(command.RefreshToken.Trim());
        var existing = await repository.FindByHashAsync(hash, ct);

        if (existing is null || existing.RevokedAt is not null)
            return true;

        existing.RevokedAt = DateTime.UtcNow;
        await repository.UpdateAsync(existing, ct);
        return true;
    }
}
