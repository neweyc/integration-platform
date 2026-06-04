using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Auth;

public record ListUsersCommand(Guid TenantId) : ICommand<ListUsersResult>;

public record ListUsersResult(IReadOnlyList<UserItem> Users);

public record UserItem(Guid Id, string Email, string Role, DateTime CreatedAt);

public interface IUserListRepository
{
    Task<IReadOnlyList<User>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);
}

public class ListUsersHandler(IUserListRepository repository)
    : ICommandHandler<ListUsersCommand, ListUsersResult>
{
    public async Task<ListUsersResult> HandleAsync(ListUsersCommand command, CancellationToken ct = default)
    {
        var users = await repository.ListByTenantAsync(command.TenantId, ct);

        return new ListUsersResult(users.Select(u =>
            new UserItem(u.Id, u.Email, u.Role.ToString(), u.CreatedAt)).ToList());
    }
}
