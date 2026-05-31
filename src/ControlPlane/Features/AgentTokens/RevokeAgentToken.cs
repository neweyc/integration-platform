using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public record RevokeAgentTokenCommand(Guid TenantId, Guid TokenId) : ICommand<bool>;

public interface IAgentTokenDeleteRepository
{
    Task<AgentToken?> FindAsync(Guid tenantId, Guid tokenId, CancellationToken ct = default);
    Task DeleteAsync(AgentToken token, CancellationToken ct = default);
}

public class RevokeAgentTokenHandler(IAgentTokenDeleteRepository repository)
    : ICommandHandler<RevokeAgentTokenCommand, bool>
{
    public async Task<bool> HandleAsync(RevokeAgentTokenCommand command, CancellationToken ct = default)
    {
        var token = await repository.FindAsync(command.TenantId, command.TokenId, ct);

        if (token is null)
            throw new NotFoundException("Agent token not found.");

        await repository.DeleteAsync(token, ct);
        return true;
    }
}
