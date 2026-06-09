using ControlPlane.Features.IntegrationPackages.Scanning;
using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

// Sets an integration's active package version (rollback / roll-forward). Kept separate from the
// general update so it changes only the pin — a normal edit can never alter the package version, and
// this can never reconcile triggers.
public record RepointIntegrationCommand(Guid TenantId, Guid IntegrationId, Guid PackageId)
    : ICommand<bool>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        result is true
            ? new(AuditAction.IntegrationUpdated, "Integration", IntegrationId.ToString(),
                $"Set active package version to {PackageId}")
            : null;
}

public interface IIntegrationRepointRepository
{
    Task<Integration?> GetByIdAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default);
    Task<AssemblyPackage?> GetPackageAsync(Guid tenantId, Guid packageId, CancellationToken ct = default);
    Task SaveAsync(Integration integration, CancellationToken ct = default);
}

public class RepointIntegrationHandler(
    IIntegrationRepointRepository repository,
    IAssemblyScanner scanner)
    : ICommandHandler<RepointIntegrationCommand, bool>
{
    public async Task<bool> HandleAsync(RepointIntegrationCommand command, CancellationToken ct = default)
    {
        var integration = await repository.GetByIdAsync(command.TenantId, command.IntegrationId, ct);
        if (integration is null)
            throw new NotFoundException($"Integration '{command.IntegrationId}' not found.");

        var package = await repository.GetPackageAsync(command.TenantId, command.PackageId, ct);
        if (package is null)
            throw new NotFoundException($"Package '{command.PackageId}' not found.");

        // Only allow pinning to a package that actually contains this integration's class. Existence
        // alone is not enough — a direct API caller could otherwise pin to an unrelated package (or a
        // version where the class was renamed/removed), leaving the integration unable to load at run time.
        var discovered = scanner.ScanZip(package.Data);
        if (!discovered.Any(d => string.Equals(d.ClassName, integration.ClassName, StringComparison.Ordinal)))
            throw new ValidationException(
                $"Package '{package.Name}' {package.Version} does not contain class '{integration.ClassName}'.");

        integration.PackageId = command.PackageId;
        integration.UpdatedAt = DateTime.UtcNow;
        await repository.SaveAsync(integration, ct);
        return true;
    }
}
