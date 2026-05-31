using ControlPlane.Infrastructure;

namespace ControlPlane.Features.Integrations;

public record ListIntegrationsCommand(Guid TenantId, string? Environment) : ICommand<ListIntegrationsResult>;

public record ListIntegrationsResult(IReadOnlyList<CreateIntegrationResult> Integrations);

public class ListIntegrationsHandler(IIntegrationReadRepository repository)
    : ICommandHandler<ListIntegrationsCommand, ListIntegrationsResult>
{
    public async Task<ListIntegrationsResult> HandleAsync(ListIntegrationsCommand command, CancellationToken ct = default)
    {
        var integrations = await repository.ListAsync(command.TenantId, command.Environment, ct);

        var results = integrations
            .Select(CreateIntegrationHandler.ToResult)
            .ToList();

        return new ListIntegrationsResult(results);
    }
}
