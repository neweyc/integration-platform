using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

// Surfaces integrations whose required capability tags no currently-live agent in their environment
// offers — i.e. work that will queue forever because nothing can claim it. This is the visibility
// half of capability routing: a tag typo or an offline specialized agent must not fail silently.
public record GetUnroutableIntegrationsCommand(Guid TenantId) : ICommand<UnroutableIntegrationsResult>;

public record UnroutableIntegrationsResult(IReadOnlyList<UnroutableIntegration> Integrations);

public record UnroutableIntegration(
    Guid Id,
    string Name,
    string Slug,
    string Environment,
    IReadOnlyList<string> RequiredTags);

public interface IRoutingHealthRepository
{
    // Enabled integrations for the tenant (callers filter to the tag-gated ones).
    Task<IReadOnlyList<Integration>> ListEnabledIntegrationsAsync(Guid tenantId, CancellationToken ct = default);

    // Agents seen since liveSince, with the capabilities they reported.
    Task<IReadOnlyList<AgentHeartbeat>> ListLiveAgentsAsync(Guid tenantId, DateTime liveSince, CancellationToken ct = default);
}

public class GetUnroutableIntegrationsHandler(IRoutingHealthRepository repository)
    : ICommandHandler<GetUnroutableIntegrationsCommand, UnroutableIntegrationsResult>
{
    // Matches the staleness window used by the agent status view.
    private static readonly TimeSpan AgentLiveWindow = TimeSpan.FromMinutes(2);

    public async Task<UnroutableIntegrationsResult> HandleAsync(
        GetUnroutableIntegrationsCommand command,
        CancellationToken ct = default)
    {
        var liveSince = DateTime.UtcNow - AgentLiveWindow;
        var agents = await repository.ListLiveAgentsAsync(command.TenantId, liveSince, ct);
        var integrations = await repository.ListEnabledIntegrationsAsync(command.TenantId, ct);

        // The tag sets offered by live agents, grouped by environment.
        var offeredByEnvironment = agents
            .GroupBy(a => a.Environment, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Tags).ToList(), StringComparer.OrdinalIgnoreCase);

        var unroutable = integrations
            .Where(i => i.RequiredTags.Length > 0)
            .Where(i => !CanBeRoutedSomewhere(i, offeredByEnvironment))
            .Select(i => new UnroutableIntegration(i.Id, i.Name, i.Slug, i.Environment, i.RequiredTags))
            .ToList();

        return new UnroutableIntegrationsResult(unroutable);
    }

    private static bool CanBeRoutedSomewhere(
        Integration integration,
        IReadOnlyDictionary<string, List<string[]>> offeredByEnvironment) =>
        offeredByEnvironment.TryGetValue(integration.Environment, out var offeredSets)
        && offeredSets.Any(offered => TagSet.IsSatisfiedBy(integration.RequiredTags, offered));
}
