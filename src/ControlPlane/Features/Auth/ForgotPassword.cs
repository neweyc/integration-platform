using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Auth;

// Starts a password reset. Always reports success regardless of whether the email matches a user,
// so the endpoint can't be used to discover which emails have accounts.
public record ForgotPasswordCommand(string Email) : ICommand<bool>;

public class ForgotPasswordHandler(
    IPasswordResetRepository repository,
    IPasswordResetNotifier notifier)
    : ICommandHandler<ForgotPasswordCommand, bool>
{
    // How long a reset link stays valid.
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    public async Task<bool> HandleAsync(ForgotPasswordCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Email))
            throw new ValidationException("Email is required.");

        var user = await repository.GetUserByEmailAsync(command.Email.Trim().ToLowerInvariant(), ct);

        // Unknown email: do nothing, but still report success (no user enumeration).
        if (user is null)
            return true;

        var plaintext = PasswordResetTokens.Generate();

        await repository.AddAsync(new PasswordResetToken
        {
            UserId = user.Id,
            TenantId = user.TenantId,
            TokenHash = PasswordResetTokens.Hash(plaintext),
            ExpiresAt = DateTime.UtcNow.Add(TokenLifetime)
        }, ct);

        await notifier.SendResetLinkAsync(user.Email, plaintext, ct);

        return true;
    }
}
