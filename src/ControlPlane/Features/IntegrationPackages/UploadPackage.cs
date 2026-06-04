using System.IO.Compression;
using System.Security.Cryptography;
using ControlPlane.Features.IntegrationPackages.Scanning;
using ControlPlane.Features.Integrations;
using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Features.IntegrationPackages;

public record UploadPackageCommand(
    Guid TenantId,
    string Name,
    string Version,
    string FileName,
    byte[] Data) : ICommand<PackageMetadata>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        new(AuditAction.PackageUploaded, "Package",
            (result as PackageMetadata)?.Id.ToString(), $"Uploaded package '{Name}' v{Version}");
}

public record PackageMetadata(
    Guid Id,
    string Name,
    string Version,
    string FileName,
    long SizeBytes,
    string Sha256Hash,
    DateTime CreatedAt);

public interface IPackageRepository
{
    Task<bool> VersionExistsAsync(
        Guid tenantId,
        string name,
        string version,
        CancellationToken ct = default);

    Task<AssemblyPackage> CreateAsync(AssemblyPackage package, CancellationToken ct = default);
}

public class UploadPackageHandler(
    IPackageRepository repository,
    IAssemblyScanner scanner,
    IIntegrationRepository integrationRepository)
    : ICommandHandler<UploadPackageCommand, PackageMetadata>
{
    private const int MaxPackageSizeBytes = 100 * 1024 * 1024;

    public async Task<PackageMetadata> HandleAsync(UploadPackageCommand command, CancellationToken ct = default)
    {
        Validate(command);

        if (await repository.VersionExistsAsync(command.TenantId, command.Name, command.Version, ct))
            throw new ConflictException($"Package '{command.Name}' version '{command.Version}' already exists.");

        var package = new AssemblyPackage
        {
            TenantId = command.TenantId,
            Name = command.Name.Trim(),
            Version = command.Version.Trim(),
            FileName = Path.GetFileName(command.FileName),
            Data = command.Data,
            SizeBytes = command.Data.LongLength,
            Sha256Hash = Convert.ToHexString(SHA256.HashData(command.Data)).ToLowerInvariant()
        };

        var created = await repository.CreateAsync(package, ct);

        // Auto-provision integrations from code attributes
        var discovered = scanner.ScanZip(command.Data);
        foreach (var integration in discovered)
        {
            await integrationRepository.UpsertBySlugAsync(new Integration
            {
                TenantId = command.TenantId,
                Name = integration.Name,
                Slug = integration.Slug,
                Description = integration.Description,
                Environment = "production", // Default to production for auto-provisioning
                TriggerType = integration.TriggerType,
                CronExpression = integration.CronExpression,
                ClassName = integration.ClassName,
                TimeoutSeconds = integration.TimeoutSeconds,
                RetryMaxAttempts = integration.RetryMaxAttempts ?? 0,
                RetryBackoffSeconds = integration.RetryBackoffSeconds,
                PackageId = created.Id,
                Status = IntegrationStatus.Enabled
            }, ct);
        }

        return ToMetadata(created);
    }

    internal static PackageMetadata ToMetadata(AssemblyPackage package) =>
        new(
            package.Id,
            package.Name,
            package.Version,
            package.FileName,
            package.SizeBytes,
            package.Sha256Hash,
            package.CreatedAt);

    private static void Validate(UploadPackageCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ValidationException("Package name is required.");

        if (command.Name.Length > 200)
            throw new ValidationException("Package name is too long.");

        if (!System.Text.RegularExpressions.Regex.IsMatch(command.Name, @"^[A-Za-z0-9_.-]+$"))
            throw new ValidationException("Package name may only contain letters, numbers, dots, underscores, and hyphens.");

        if (string.IsNullOrWhiteSpace(command.Version))
            throw new ValidationException("Package version is required.");

        if (command.Version.Length > 50)
            throw new ValidationException("Package version is too long.");

        if (!System.Text.RegularExpressions.Regex.IsMatch(command.Version, @"^[A-Za-z0-9_.+-]+$"))
            throw new ValidationException("Package version may only contain letters, numbers, dots, underscores, plus signs, and hyphens.");

        if (string.IsNullOrWhiteSpace(command.FileName))
            throw new ValidationException("Package filename is required.");

        if (!Path.GetExtension(command.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Package file must be a .zip archive.");

        if (command.Data.Length == 0)
            throw new ValidationException("Package file cannot be empty.");

        if (command.Data.Length > MaxPackageSizeBytes)
            throw new ValidationException("Package file cannot exceed 100 MB.");

        ValidateZip(command.Data);
    }

    private static void ValidateZip(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            if (archive.Entries.Count == 0)
                throw new ValidationException("Package archive cannot be empty.");

            if (!archive.Entries.Any(e => Path.GetExtension(e.Name).Equals(".dll", StringComparison.OrdinalIgnoreCase)))
                throw new ValidationException("Package archive must contain at least one .dll file.");
        }
        catch (InvalidDataException)
        {
            throw new ValidationException("Package file must be a valid .zip archive.");
        }
    }
}
