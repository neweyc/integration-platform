using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;
using System.Security.Cryptography;

namespace ControlPlane.Features.Invitations;

public record InviteUserCommand(string Email, UserRole Role) : ICommand<InviteUserResult>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        new(AuditAction.UserInvited, "Invitation",
            (result as InviteUserResult)?.InvitationId.ToString(), $"Invited {Email} as {Role}");
}

public record InviteUserResult(Guid InvitationId, string Email, string Token, DateTime ExpiresAt);

public interface IInvitationRepository
{
    Task<Invitation> CreateAsync(Invitation invitation, CancellationToken ct = default);
    Task<Invitation?> GetByTokenAsync(string token, CancellationToken ct = default);
    Task UpdateAsync(Invitation invitation, CancellationToken ct = default);
}

public class InviteUserHandler(IInvitationRepository repository, ICurrentUser currentUser)
    : ICommandHandler<InviteUserCommand, InviteUserResult>
{
    public async Task<InviteUserResult> HandleAsync(InviteUserCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Email))
            throw new ValidationException("Email is required.");

        var token = GenerateSecureToken();
        var expiresAt = DateTime.UtcNow.AddDays(7);

        var invitation = new Invitation
        {
            TenantId = currentUser.TenantId,
            Email = command.Email.ToLowerInvariant(),
            Role = command.Role,
            Token = token,
            ExpiresAt = expiresAt
        };

        var created = await repository.CreateAsync(invitation, ct);

        return new InviteUserResult(created.Id, created.Email, created.Token, created.ExpiresAt);
    }

    private static string GenerateSecureToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
