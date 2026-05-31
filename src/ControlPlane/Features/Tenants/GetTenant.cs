using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Tenants;

public record GetTenantCommand(Guid Id) : ICommand<GetTenantResult?>;

public record GetTenantResult(Guid Id, string Name, string Slug, TenantStatus Status, DateTime CreatedAt);

public interface ITenantReadRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default);
}

public class GetTenantHandler(ITenantReadRepository repository)
    : ICommandHandler<GetTenantCommand, GetTenantResult?>
{
    public async Task<GetTenantResult?> HandleAsync(GetTenantCommand command, CancellationToken ct = default)
    {
        var tenant = await repository.GetByIdAsync(command.Id, ct);

        if (tenant is null)
            return null;

        return new GetTenantResult(tenant.Id, tenant.Name, tenant.Slug, tenant.Status, tenant.CreatedAt);
    }
}
