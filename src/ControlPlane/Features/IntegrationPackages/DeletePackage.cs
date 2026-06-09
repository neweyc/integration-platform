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

    // Names of integrations currently pinned to this package. Used to block deletion of an in-use
    // version: Integration.PackageId is OnDelete(SetNull), so deleting it would silently un-pin the
    // integration and it would fail at runtime (falling back to a local path that is not present).
    Task<IReadOnlyList<string>> ListPinnedIntegrationNamesAsync(
        Guid tenantId, Guid packageId, CancellationToken ct = default);
}

public class DeletePackageHandler(IPackageDeleteRepository repository)
    : ICommandHandler<DeletePackageCommand, bool>
{
    public async Task<bool> HandleAsync(DeletePackageCommand command, CancellationToken ct = default)
    {
        var pinnedTo = await repository.ListPinnedIntegrationNamesAsync(command.TenantId, command.PackageId, ct);
        if (pinnedTo.Count > 0)
            throw new ConflictException(
                $"This package is the active version for {pinnedTo.Count} integration(s): " +
                $"{string.Join(", ", pinnedTo)}. Repoint them to another version before deleting it.");

        return await repository.DeleteAsync(command.TenantId, command.PackageId, ct);
    }
}
