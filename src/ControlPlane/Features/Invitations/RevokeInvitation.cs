using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Features.Invitations;

public record RevokeInvitationCommand(Guid TenantId, Guid InvitationId) : ICommand<bool>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        result is true
            ? new(AuditAction.InvitationRevoked, "Invitation", InvitationId.ToString(), "Revoked invitation")
            : null;
}

public interface IInvitationRevocationRepository
{
    Task<bool> RevokePendingAsync(Guid tenantId, Guid invitationId, CancellationToken ct = default);
}

public class RevokeInvitationHandler(IInvitationRevocationRepository repository)
    : ICommandHandler<RevokeInvitationCommand, bool>
{
    public Task<bool> HandleAsync(RevokeInvitationCommand command, CancellationToken ct = default) =>
        repository.RevokePendingAsync(command.TenantId, command.InvitationId, ct);
}
