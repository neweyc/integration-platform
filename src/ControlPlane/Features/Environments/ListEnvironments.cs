using ControlPlane.Features.Billing;
using ControlPlane.Features.Tenants;
using ControlPlane.Infrastructure;

namespace ControlPlane.Features.Environments;

public record ListEnvironmentsCommand(Guid TenantId) : ICommand<ListEnvironmentsResult>;

// MaxEnvironments is the plan's environment cap; null means unlimited (paid plans). The UI uses it to
// disable "New environment" at the cap.
public record ListEnvironmentsResult(IReadOnlyList<EnvironmentDto> Environments, int? MaxEnvironments);

public class ListEnvironmentsHandler(
    IEnvironmentReadRepository repository,
    ITenantReadRepository tenants,
    BillingPlanCatalog planCatalog)
    : ICommandHandler<ListEnvironmentsCommand, ListEnvironmentsResult>
{
    public async Task<ListEnvironmentsResult> HandleAsync(ListEnvironmentsCommand command, CancellationToken ct = default)
    {
        var environments = await repository.ListAsync(command.TenantId, ct);
        var tenant = await tenants.GetByIdAsync(command.TenantId, ct);

        // Surface the plan cap so the UI can disable adding at the limit; int.MaxValue → unlimited (null).
        int? maxEnvironments = tenant is null ? null : planCatalog.MaxEnvironmentsFor(tenant.Plan);
        if (maxEnvironments == int.MaxValue)
            maxEnvironments = null;

        return new ListEnvironmentsResult(environments.Select(EnvironmentDto.From).ToList(), maxEnvironments);
    }
}
