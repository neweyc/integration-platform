using ControlPlane.Infrastructure;

namespace ControlPlane.Features.UserTokens;

public record RevokeUserTokenCommand(Guid TenantId, Guid UserId, Guid TokenId) : ICommand<bool>;

public class RevokeUserTokenHandler(IUserTokenRepository repository)
    : ICommandHandler<RevokeUserTokenCommand, bool>
{
    public async Task<bool> HandleAsync(RevokeUserTokenCommand command, CancellationToken ct = default)
    {
        await repository.DeleteAsync(command.TenantId, command.UserId, command.TokenId, ct);
        return true;
    }
}
