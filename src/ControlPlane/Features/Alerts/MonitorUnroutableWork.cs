using ControlPlane.Features.Integrations;
using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Alerts;

// Sweeps every tenant's enabled, tag-gated integrations and compares their required capabilities to the
// tags offered by currently-live agents. Integrations that just became unroutable are returned for
// alerting and stamped (dedup); integrations that recovered are un-stamped so a future outage re-alerts.
public record MonitorUnroutableWorkCommand(DateTime Now) : ICommand<MonitorUnroutableWorkResult>;

public record MonitorUnroutableWorkResult(IReadOnlyList<UnroutableIntegrationAlert> NewlyUnroutable, int RecoveredCount);

public interface IUnroutableAlertRepository
{
    // Tracked (not AsNoTracking) so UnroutableAlertedAt changes persist on SaveChanges.
    Task<IReadOnlyList<Integration>> ListEnabledTagGatedIntegrationsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AgentHeartbeat>> ListLiveAgentsAsync(DateTime liveSince, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public class MonitorUnroutableWorkHandler(IUnroutableAlertRepository repository)
    : ICommandHandler<MonitorUnroutableWorkCommand, MonitorUnroutableWorkResult>
{
    // Matches the agent staleness window used elsewhere (status view, GetUnroutableIntegrations).
    private static readonly TimeSpan AgentLiveWindow = TimeSpan.FromMinutes(2);

    public async Task<MonitorUnroutableWorkResult> HandleAsync(MonitorUnroutableWorkCommand command, CancellationToken ct = default)
    {
        var liveSince = command.Now - AgentLiveWindow;
        var integrations = await repository.ListEnabledTagGatedIntegrationsAsync(ct);
        var agents = await repository.ListLiveAgentsAsync(liveSince, ct);

        // Tag sets offered by live agents, keyed by (tenant, environment).
        var offered = new Dictionary<string, List<string[]>>();
        foreach (var agent in agents)
        {
            var key = OfferKey(agent.TenantId, agent.Environment);
            if (!offered.TryGetValue(key, out var sets))
            {
                sets = [];
                offered[key] = sets;
            }
            sets.Add(agent.Tags);
        }

        var newlyUnroutable = new List<UnroutableIntegrationAlert>();
        var recovered = 0;

        foreach (var integration in integrations)
        {
            var routable = offered.TryGetValue(OfferKey(integration.TenantId, integration.Environment), out var sets)
                && sets.Any(set => TagSet.IsSatisfiedBy(integration.RequiredTags, set));

            if (!routable)
            {
                // Alert once per transition into the unroutable state.
                if (integration.UnroutableAlertedAt is null)
                {
                    integration.UnroutableAlertedAt = command.Now;
                    newlyUnroutable.Add(new UnroutableIntegrationAlert(
                        integration.TenantId,
                        integration.Id,
                        integration.Name,
                        integration.Slug,
                        integration.Environment,
                        integration.RequiredTags,
                        command.Now));
                }
            }
            else if (integration.UnroutableAlertedAt is not null)
            {
                integration.UnroutableAlertedAt = null;
                recovered++;
            }
        }

        await repository.SaveChangesAsync(ct);
        return new MonitorUnroutableWorkResult(newlyUnroutable, recovered);
    }

    private static string OfferKey(Guid tenantId, string environment) =>
        $"{tenantId}|{environment.ToLowerInvariant()}";
}
