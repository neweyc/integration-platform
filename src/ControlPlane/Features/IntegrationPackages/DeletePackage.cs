using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Features.IntegrationPackages;

public record DeletePackageCommand(Guid TenantId, Guid PackageId) : ICommand<bool>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        result is true
            ? new(AuditAction.PackageDeleted, "Package", PackageId.ToString(), "Deleted package")
            : null;
}

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
