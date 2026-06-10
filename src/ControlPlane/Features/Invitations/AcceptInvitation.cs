using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;
using ControlPlane.Features.Auth;

namespace ControlPlane.Features.Invitations;

public record AcceptInvitationCommand(string Token, string Password) : ICommand<AcceptInvitationResult>;

public record AcceptInvitationResult(
    Guid UserId, string Email, string Token,
    string RefreshToken, DateTime RefreshTokenExpiresAt);

public class AcceptInvitationHandler(
    IInvitationRepository invitationRepository,
    IUserRepository userRepository,
    IAuthTokenIssuer issuer,
    IAuditRecorder auditRecorder)
    : ICommandHandler<AcceptInvitationCommand, AcceptInvitationResult>
{
    public async Task<AcceptInvitationResult> HandleAsync(AcceptInvitationCommand command, CancellationToken ct = default)
    {
        var invitation = await invitationRepository.GetByTokenAsync(command.Token, ct);

        if (invitation is null || invitation.AcceptedAt.HasValue || invitation.ExpiresAt < DateTime.UtcNow)
            throw new ValidationException("Invitation is invalid or has expired.");

        if (string.IsNullOrWhiteSpace(command.Password) || command.Password.Length < 8)
            throw new ValidationException("Password must be at least 8 characters.");

        var user = new User
        {
            TenantId = invitation.TenantId,
            Email = invitation.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(command.Password),
            Role = invitation.Role
        };

        var createdUser = await userRepository.CreateAsync(user, ct);

        invitation.AcceptedAt = DateTime.UtcNow;
        await invitationRepository.UpdateAsync(invitation, ct);

        var tokens = await issuer.IssueAsync(createdUser, ct);

        // The actor is the newly-created user, who has no authenticated context yet — record explicitly.
        await auditRecorder.RecordAsync(new AuditDescriptor(
            AuditAction.InvitationAccepted, "User", createdUser.Id.ToString(),
            $"Accepted invitation as {createdUser.Role}",
            ExplicitTenantId: createdUser.TenantId,
            ExplicitActorUserId: createdUser.Id,
            ExplicitActorEmail: createdUser.Email), ct);

        return new AcceptInvitationResult(
            createdUser.Id, createdUser.Email, tokens.AccessToken,
            tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
    }
}
