using ControlPlane.Infrastructure;

namespace ControlPlane.Features.Auth;

// Completes a password reset: validates the token, sets the new password, and revokes every active
// session for that user so a compromised password can't keep a stolen session alive.
public record ResetPasswordCommand(string Token, string NewPassword) : ICommand<bool>;

public class ResetPasswordHandler(
    IPasswordResetRepository repository,
    IRefreshTokenRepository refreshTokenRepository)
    : ICommandHandler<ResetPasswordCommand, bool>
{
    public async Task<bool> HandleAsync(ResetPasswordCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
            throw new ValidationException("A reset token is required.");

        if (string.IsNullOrWhiteSpace(command.NewPassword) || command.NewPassword.Length < 8)
            throw new ValidationException("Password must be at least 8 characters.");

        var hash = PasswordResetTokens.Hash(command.Token.Trim());
        var token = await repository.FindUnusedByHashAsync(hash, ct);

        // Unknown, already-used, and expired tokens all report the same generic error.
        if (token is null || token.ExpiresAt < DateTime.UtcNow)
            throw new ValidationException("This password reset link is invalid or has expired.");

        var newPasswordHash = BCrypt.Net.BCrypt.HashPassword(command.NewPassword);
        await repository.ConsumeAndSetPasswordAsync(token, newPasswordHash, ct);

        // Force re-login everywhere: any refresh tokens issued before the reset are now invalid.
        await refreshTokenRepository.RevokeAllForUserAsync(token.UserId, DateTime.UtcNow, ct);

        return true;
    }
}
