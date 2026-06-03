using ControlPlane.Infrastructure;

namespace ControlPlane.Features.UserTokens;

public record ListUserTokensCommand(Guid TenantId, Guid UserId) : ICommand<ListUserTokensResult>;

public record ListUserTokensResult(IReadOnlyList<UserTokenItem> Tokens);

public record UserTokenItem(Guid Id, string Name, DateTime CreatedAt, DateTime? LastUsedAt);

public class ListUserTokensHandler(IUserTokenRepository repository)
    : ICommandHandler<ListUserTokensCommand, ListUserTokensResult>
{
    public async Task<ListUserTokensResult> HandleAsync(ListUserTokensCommand command, CancellationToken ct = default)
    {
        var tokens = await repository.ListAsync(command.TenantId, command.UserId, ct);
        var items = tokens.Select(t => new UserTokenItem(t.Id, t.Name, t.CreatedAt, t.LastUsedAt)).ToList();
        return new ListUserTokensResult(items);
    }
}
