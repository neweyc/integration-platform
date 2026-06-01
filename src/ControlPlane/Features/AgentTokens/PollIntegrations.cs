using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

// Claims due scheduled integrations for an environment.
public record PollIntegrationsCommand(Guid TenantId, string Environment) : ICommand<PollIntegrationsResult>;

public record PollIntegrationsResult(IReadOnlyList<AgentIntegrationItem> Integrations);

public record AgentIntegrationItem(
    Guid Id,
    string Name,
    string Slug,
    TriggerType TriggerType,
    string? CronExpression,
    string ClassName);

public interface IPollRepository
{
    Task<IReadOnlyList<Integration>> ClaimDueScheduledAsync(Guid tenantId, string environment, DateTime now, CancellationToken ct = default);
}

public class PollIntegrationsHandler(IPollRepository repository)
    : ICommandHandler<PollIntegrationsCommand, PollIntegrationsResult>
{
    public async Task<PollIntegrationsResult> HandleAsync(PollIntegrationsCommand command, CancellationToken ct = default)
    {
        var integrations = await repository.ClaimDueScheduledAsync(
            command.TenantId,
            command.Environment,
            DateTime.UtcNow,
            ct);

        var items = integrations
            .Select(i => new AgentIntegrationItem(i.Id, i.Name, i.Slug, i.TriggerType, i.CronExpression, i.ClassName))
            .ToList();

        return new PollIntegrationsResult(items);
    }
}
