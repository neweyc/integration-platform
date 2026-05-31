using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

// Returns all enabled integrations for an environment so the agent can determine which are due.
public record PollIntegrationsCommand(Guid TenantId, string Environment) : ICommand<PollIntegrationsResult>;

public record PollIntegrationsResult(IReadOnlyList<AgentIntegrationItem> Integrations);

public record AgentIntegrationItem(
    Guid Id,
    string Name,
    string Slug,
    TriggerType TriggerType,
    string? CronExpression);

public interface IPollRepository
{
    Task<IReadOnlyList<Integration>> ListEnabledAsync(Guid tenantId, string environment, CancellationToken ct = default);
}

public class PollIntegrationsHandler(IPollRepository repository)
    : ICommandHandler<PollIntegrationsCommand, PollIntegrationsResult>
{
    public async Task<PollIntegrationsResult> HandleAsync(PollIntegrationsCommand command, CancellationToken ct = default)
    {
        var integrations = await repository.ListEnabledAsync(command.TenantId, command.Environment, ct);

        var items = integrations
            .Select(i => new AgentIntegrationItem(i.Id, i.Name, i.Slug, i.TriggerType, i.CronExpression))
            .ToList();

        return new PollIntegrationsResult(items);
    }
}
