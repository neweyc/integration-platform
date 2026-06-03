using ControlPlane.Infrastructure;

namespace ControlPlane.Features.UserTokens;

public record RevokeUserTokenCommand(Guid TenantId, Guid TokenId) : ICommand<bool>;

public class RevokeUserTokenHandler(IUserTokenRepository repository)
    : ICommandHandler<RevokeUserTokenCommand, bool>
{
    public async Task<bool> HandleAsync(RevokeUserTokenCommand command, CancellationToken ct = default)
    {
        await repository.DeleteAsync(command.TenantId, command.TokenId, ct);
        return true;
    }
}
