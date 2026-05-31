using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.IntegrationPackages;

public record GetPackageCommand(Guid TenantId, Guid PackageId) : ICommand<PackageMetadata?>;

public record DownloadPackageCommand(Guid TenantId, Guid PackageId) : ICommand<DownloadPackageResult?>;

public record DownloadPackageResult(
    string FileName,
    string ContentType,
    byte[] Data);

public class GetPackageHandler(IPackageReadRepository repository)
    : ICommandHandler<GetPackageCommand, PackageMetadata?>
{
    public async Task<PackageMetadata?> HandleAsync(GetPackageCommand command, CancellationToken ct = default)
    {
        var package = await repository.GetAsync(command.TenantId, command.PackageId, ct);
        return package is null ? null : UploadPackageHandler.ToMetadata(package);
    }
}

public class DownloadPackageHandler(IPackageReadRepository repository)
    : ICommandHandler<DownloadPackageCommand, DownloadPackageResult?>
{
    public async Task<DownloadPackageResult?> HandleAsync(DownloadPackageCommand command, CancellationToken ct = default)
    {
        var package = await repository.GetAsync(command.TenantId, command.PackageId, ct);

        return package is null
            ? null
            : new DownloadPackageResult(package.FileName, "application/zip", package.Data);
    }
}
