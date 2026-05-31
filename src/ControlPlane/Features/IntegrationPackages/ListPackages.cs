using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.IntegrationPackages;

public record ListPackagesCommand(Guid TenantId) : ICommand<ListPackagesResult>;

public record ListPackagesResult(IReadOnlyList<PackageMetadata> Packages);

public interface IPackageReadRepository
{
    Task<IReadOnlyList<AssemblyPackage>> ListAsync(Guid tenantId, CancellationToken ct = default);
    Task<AssemblyPackage?> GetAsync(Guid tenantId, Guid packageId, CancellationToken ct = default);
}

public class ListPackagesHandler(IPackageReadRepository repository)
    : ICommandHandler<ListPackagesCommand, ListPackagesResult>
{
    public async Task<ListPackagesResult> HandleAsync(ListPackagesCommand command, CancellationToken ct = default)
    {
        var packages = await repository.ListAsync(command.TenantId, ct);
        return new ListPackagesResult(packages.Select(UploadPackageHandler.ToMetadata).ToList());
    }
}
