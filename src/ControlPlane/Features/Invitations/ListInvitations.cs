using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Invitations;

public record ListInvitationsCommand(Guid TenantId) : ICommand<ListInvitationsResult>;

public record ListInvitationsResult(IReadOnlyList<InvitationItem> Invitations);

public record InvitationItem(
    Guid Id,
    string Email,
    string Role,
    DateTime ExpiresAt,
    DateTime? AcceptedAt);

public interface IInvitationReadRepository
{
    Task<IReadOnlyList<Invitation>> ListPendingByTenantAsync(Guid tenantId, CancellationToken ct = default);
}

public class ListInvitationsHandler(IInvitationReadRepository repository)
    : ICommandHandler<ListInvitationsCommand, ListInvitationsResult>
{
    public async Task<ListInvitationsResult> HandleAsync(ListInvitationsCommand command, CancellationToken ct = default)
    {
        var invitations = await repository.ListPendingByTenantAsync(command.TenantId, ct);

        return new ListInvitationsResult(invitations.Select(i =>
            new InvitationItem(i.Id, i.Email, i.Role.ToString(), i.ExpiresAt, i.AcceptedAt)).ToList());
    }
}
