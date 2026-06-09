using ControlPlane.Features.IntegrationPackages.Scanning;
using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Features.IntegrationPackages;

// Activates a package version for its entire package. Integrations are versioned at the package level:
// every integration currently running from any version of the same package name is repointed to this
// version together, so a package's integrations can never end up split across versions. There is no
// per-integration pin to set independently — that is the whole point of the package-level model.
public record ActivatePackageVersionCommand(Guid TenantId, Guid PackageId)
    : ICommand<ActivatePackageVersionResult>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        result is ActivatePackageVersionResult r
            ? new(AuditAction.PackageActivated, "Package", PackageId.ToString(),
                $"Activated {r.PackageName} {r.Version} for {r.Activated.Count} integration(s)")
            : null;
}

// Activated = integrations now running this version (including any already on it). Skipped =
// integrations left on their old version because this version no longer contains their class
// (a renamed/removed class cannot run from here), so they are reported rather than silently broken.
public record ActivatePackageVersionResult(
    string PackageName,
    string Version,
    IReadOnlyList<string> Activated,
    IReadOnlyList<string> Skipped);

public interface IPackageActivationRepository
{
    Task<AssemblyPackage?> GetPackageAsync(Guid tenantId, Guid packageId, CancellationToken ct = default);

    // Integrations currently pinned to any version of the given package name — the set that moves
    // together when a version is activated. Returns tracked entities so SaveAsync persists the change.
    Task<IReadOnlyList<Integration>> ListIntegrationsForPackageNameAsync(
        Guid tenantId, string packageName, CancellationToken ct = default);

    Task SaveAsync(CancellationToken ct = default);
}

public class ActivatePackageVersionHandler(
    IPackageActivationRepository repository,
    IAssemblyScanner scanner)
    : ICommandHandler<ActivatePackageVersionCommand, ActivatePackageVersionResult>
{
    public async Task<ActivatePackageVersionResult> HandleAsync(
        ActivatePackageVersionCommand command, CancellationToken ct = default)
    {
        var package = await repository.GetPackageAsync(command.TenantId, command.PackageId, ct);
        if (package is null)
            throw new NotFoundException($"Package '{command.PackageId}' not found.");

        // The classes this version actually provides. An integration can only run from a version that
        // contains its class, so a sibling whose class is absent here is skipped, not repointed.
        var available = scanner.ScanZip(package.Data)
            .Select(d => d.ClassName)
            .ToHashSet(StringComparer.Ordinal);

        var siblings = await repository.ListIntegrationsForPackageNameAsync(command.TenantId, package.Name, ct);

        var activated = new List<string>();
        var skipped = new List<string>();
        var now = DateTime.UtcNow;

        foreach (var integration in siblings)
        {
            if (integration.PackageId == command.PackageId)
            {
                activated.Add(integration.Name); // already on this version
                continue;
            }

            if (!available.Contains(integration.ClassName))
            {
                skipped.Add(integration.Name);
                continue;
            }

            integration.PackageId = command.PackageId;
            integration.UpdatedAt = now;
            activated.Add(integration.Name);
        }

        await repository.SaveAsync(ct);

        return new ActivatePackageVersionResult(package.Name, package.Version, activated, skipped);
    }
}
