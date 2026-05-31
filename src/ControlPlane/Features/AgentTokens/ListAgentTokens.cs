using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public record ListAgentTokensCommand(Guid TenantId) : ICommand<ListAgentTokensResult>;

public record ListAgentTokensResult(IReadOnlyList<AgentTokenSummary> Tokens);

// Token values are never included in list responses
public record AgentTokenSummary(Guid Id, string Name, string Environment, DateTime CreatedAt);

public interface IAgentTokenReadRepository
{
    Task<IReadOnlyList<AgentToken>> ListAsync(Guid tenantId, CancellationToken ct = default);
}

public class ListAgentTokensHandler(IAgentTokenReadRepository repository)
    : ICommandHandler<ListAgentTokensCommand, ListAgentTokensResult>
{
    public async Task<ListAgentTokensResult> HandleAsync(ListAgentTokensCommand command, CancellationToken ct = default)
    {
        var tokens = await repository.ListAsync(command.TenantId, ct);

        var summaries = tokens
            .Select(t => new AgentTokenSummary(t.Id, t.Name, t.Environment, t.CreatedAt))
            .ToList();

        return new ListAgentTokensResult(summaries);
    }
}
