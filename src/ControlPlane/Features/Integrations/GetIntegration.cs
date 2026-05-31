using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

public record GetIntegrationCommand(Guid TenantId, Guid IntegrationId) : ICommand<CreateIntegrationResult?>;

public interface IIntegrationReadRepository
{
    Task<Integration?> GetByIdAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default);
    Task<IReadOnlyList<Integration>> ListAsync(Guid tenantId, string? environment, CancellationToken ct = default);
}

public class GetIntegrationHandler(IIntegrationReadRepository repository)
    : ICommandHandler<GetIntegrationCommand, CreateIntegrationResult?>
{
    public async Task<CreateIntegrationResult?> HandleAsync(GetIntegrationCommand command, CancellationToken ct = default)
    {
        var integration = await repository.GetByIdAsync(command.TenantId, command.IntegrationId, ct);

        if (integration is null)
            return null;

        return CreateIntegrationHandler.ToResult(integration);
    }
}
