using ControlPlane.Infrastructure;

namespace ControlPlane.Features.IntegrationPackages;

public record DeletePackageCommand(Guid TenantId, Guid PackageId) : ICommand<bool>;

public interface IPackageDeleteRepository
{
    Task<bool> DeleteAsync(Guid tenantId, Guid packageId, CancellationToken ct = default);
}

public class DeletePackageHandler(IPackageDeleteRepository repository)
    : ICommandHandler<DeletePackageCommand, bool>
{
    public Task<bool> HandleAsync(DeletePackageCommand command, CancellationToken ct = default)
    {
        return repository.DeleteAsync(command.TenantId, command.PackageId, ct);
    }
}
