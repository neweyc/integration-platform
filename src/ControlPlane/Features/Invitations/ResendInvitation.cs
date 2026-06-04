using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Features.Invitations;

public record ResendInvitationCommand(Guid TenantId, Guid InvitationId) : ICommand<ResendInvitationResult?>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        result is ResendInvitationResult resent
            ? new(AuditAction.InvitationResent, "Invitation", resent.InvitationId.ToString(),
                $"Resent invitation to {resent.Email} as {resent.Role}")
            : null;
}

public record ResendInvitationResult(
    Guid InvitationId,
    string Email,
    string Role,
    string Token,
    DateTime ExpiresAt);

public interface IInvitationResendRepository
{
    Task<Invitation?> ResendPendingAsync(Guid tenantId, Guid invitationId, string token, DateTime expiresAt, CancellationToken ct = default);
}

public class ResendInvitationHandler(IInvitationResendRepository repository)
    : ICommandHandler<ResendInvitationCommand, ResendInvitationResult?>
{
    public async Task<ResendInvitationResult?> HandleAsync(ResendInvitationCommand command, CancellationToken ct = default)
    {
        var token = InvitationTokenGenerator.GenerateSecureToken();
        var expiresAt = DateTime.UtcNow.AddDays(7);
        var invitation = await repository.ResendPendingAsync(command.TenantId, command.InvitationId, token, expiresAt, ct);

        return invitation is null
            ? null
            : new ResendInvitationResult(invitation.Id, invitation.Email, invitation.Role.ToString(), invitation.Token, invitation.ExpiresAt);
    }
}
