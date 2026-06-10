using System.IO.Compression;
using System.Security.Cryptography;
using ControlPlane.Features.Environments;
using ControlPlane.Features.IntegrationPackages.Scanning;
using ControlPlane.Features.Integrations;
using ControlPlane.Features.Secrets;
using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Cronos;
using Shared.Domain;

namespace ControlPlane.Features.IntegrationPackages;

public record UploadPackageCommand(
    Guid TenantId,
    string Name,
    string Version,
    string FileName,
    byte[] Data,
    IReadOnlyList<string>? RequiredSecrets = null,
    // The environment to provision integrations into and run the secret check against. Null means
    // "use the tenant's default environment" (the historical behavior); a non-null value is an
    // explicit target supplied by `serto deploy --environment` and must already exist.
    string? Environment = null) : ICommand<PackageUploadResult>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        new(AuditAction.PackageUploaded, "Package",
            (result as PackageUploadResult)?.Package.Id.ToString(), $"Uploaded package '{Name}' v{Version}");
}

public record PackageMetadata(
    Guid Id,
    string Name,
    string Version,
    string FileName,
    long SizeBytes,
    string Sha256Hash,
    DateTime CreatedAt);

public record PackageUploadResult(
    PackageMetadata Package,
    IReadOnlyList<PackageProvisioningResult> Provisioning,
    PackageSecretCheck SecretCheck);

// Compares the secret names the scanned source requires against the secrets actually
// configured in the environment the integrations are provisioned into. Advisory only:
// the names are supplied by the client scan (the server only has the compiled assembly),
// and a missing secret does not block the upload.
public record PackageSecretCheck(
    string Environment,
    IReadOnlyList<string> Required,
    IReadOnlyList<string> Satisfied,
    IReadOnlyList<string> Missing);

public record PackageProvisioningResult(
    Guid Id,
    string Name,
    string Slug,
    string Environment,
    string ClassName,
    string Action,
    Guid PackageId,
    IReadOnlyList<PackageProvisionedTriggerResult> Triggers);

public record PackageProvisionedTriggerResult(
    Guid Id,
    string Name,
    string Slug,
    string Type,
    bool Enabled,
    string Action,
    string? CronExpression = null,
    DateTime? NextRunAt = null,
    string? WebhookUrl = null,
    bool WebhookSecretPreserved = false,
    string? DeclaredCronExpression = null,
    bool CronOverridden = false,
    bool EnabledOverridden = false);

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
    IIntegrationRepository integrationRepository,
    IEncryptionService encryption,
    ISecretReadRepository secretRepository,
    IEnvironmentReadRepository environmentRepository)
    : ICommandHandler<UploadPackageCommand, PackageUploadResult>
{
    private const int MaxPackageSizeBytes = 100 * 1024 * 1024;

    public async Task<PackageUploadResult> HandleAsync(UploadPackageCommand command, CancellationToken ct = default)
    {
        Validate(command);

        var provisioningEnvironment = await ResolveProvisioningEnvironmentAsync(command, ct);

        if (await repository.VersionExistsAsync(command.TenantId, command.Name, command.Version, ct))
            throw new ConflictException($"Package '{command.Name}' version '{command.Version}' already exists.");

        var discovered = scanner.ScanZip(command.Data);
        foreach (var integration in discovered)
            ValidateDiscoveredIntegration(command.TenantId, integration, provisioningEnvironment);

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
        var metadata = ToMetadata(created);
        var tenantSlug = await integrationRepository.GetTenantSlugAsync(command.TenantId, ct);
        var provisioning = new List<PackageProvisioningResult>();

        // Auto-provision integrations from code attributes
        foreach (var integration in discovered)
        {
            var triggers = ToTriggerInputs(integration);

            var upsert = await integrationRepository.UpsertBySlugAsync(new Integration
            {
                TenantId = command.TenantId,
                Name = integration.Name,
                Slug = integration.Slug,
                Description = integration.Description,
                Environment = provisioningEnvironment,
                ClassName = integration.ClassName,
                TimeoutSeconds = integration.TimeoutSeconds,
                RetryMaxAttempts = integration.RetryMaxAttempts ?? 0,
                RetryBackoffSeconds = integration.RetryBackoffSeconds,
                PackageId = created.Id,
                Status = IntegrationStatus.Enabled
            }, CreateIntegrationHandler.BuildTriggers(command.TenantId, triggers, encryption), ct);

            provisioning.Add(ToProvisioningResult(upsert, created.Id, tenantSlug));
        }

        var secretCheck = await BuildSecretCheckAsync(command, provisioningEnvironment, ct);

        return new PackageUploadResult(metadata, provisioning, secretCheck);
    }

    // Resolves which environment the package's integrations are provisioned into and the secret check
    // runs against. An explicit `serto deploy --environment` target must already exist (provisioning into
    // a phantom environment would silently strand the integrations); when none is supplied we fall back to
    // the tenant's default environment, resolved from the registry so it always names a real environment.
    private async Task<string> ResolveProvisioningEnvironmentAsync(UploadPackageCommand command, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(command.Environment))
        {
            var requested = command.Environment.Trim();
            if (!await environmentRepository.ExistsAsync(command.TenantId, requested, ct))
                throw new ValidationException(
                    $"Environment '{requested}' does not exist. Create it first, or omit --environment to deploy to the default environment.");

            return requested;
        }

        return await environmentRepository.GetDefaultNameAsync(command.TenantId, ct)
            ?? throw new ValidationException(
                "This tenant has no environments configured. Create an environment before uploading a package.");
    }

    // Compares the client-supplied required secret names against the secrets configured in the
    // provisioning environment. Comparison is case-insensitive to match how the CLI scan dedups
    // names and the env-var-style key convention.
    private async Task<PackageSecretCheck> BuildSecretCheckAsync(
        UploadPackageCommand command, string provisioningEnvironment, CancellationToken ct)
    {
        var required = (command.RequiredSecrets ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (required.Count == 0)
            return new PackageSecretCheck(provisioningEnvironment, [], [], []);

        var configured = await secretRepository.ListAsync(command.TenantId, provisioningEnvironment, ct);
        var configuredKeys = configured.Select(secret => secret.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var satisfied = required.Where(configuredKeys.Contains).ToList();
        var missing = required.Where(name => !configuredKeys.Contains(name)).ToList();

        return new PackageSecretCheck(provisioningEnvironment, required, satisfied, missing);
    }

    private static void ValidateDiscoveredIntegration(
        Guid tenantId, DiscoveredIntegration integration, string provisioningEnvironment)
    {
        CreateIntegrationHandler.ValidateCommand(new CreateIntegrationCommand(
            tenantId,
            integration.Name,
            integration.Slug,
            integration.Description,
            provisioningEnvironment,
            integration.ClassName,
            ToTriggerInputs(integration),
            integration.TimeoutSeconds,
            integration.RetryMaxAttempts ?? 0,
            integration.RetryBackoffSeconds));
    }

    private static List<IntegrationTriggerInput> ToTriggerInputs(DiscoveredIntegration integration) =>
        integration.Triggers.Select(trigger =>
            new IntegrationTriggerInput(
                trigger.Name,
                trigger.Slug,
                trigger.Type,
                Enabled: true,
                trigger.CronExpression)).ToList();

    internal static PackageMetadata ToMetadata(AssemblyPackage package) =>
        new(
            package.Id,
            package.Name,
            package.Version,
            package.FileName,
            package.SizeBytes,
            package.Sha256Hash,
            package.CreatedAt);

    private static PackageProvisioningResult ToProvisioningResult(
        IntegrationUpsertResult upsert,
        Guid packageId,
        string? tenantSlug)
    {
        var integration = upsert.Integration;

        return new PackageProvisioningResult(
            integration.Id,
            integration.Name,
            integration.Slug,
            integration.Environment,
            integration.ClassName,
            upsert.Created ? "Created" : "Updated",
            packageId,
            upsert.Triggers.Select(t => ToProvisionedTriggerResult(integration, t, tenantSlug)).ToList());
    }

    private static PackageProvisionedTriggerResult ToProvisionedTriggerResult(
        Integration integration,
        IntegrationTriggerUpsertResult upsert,
        string? tenantSlug)
    {
        var trigger = upsert.Trigger;
        var nextRunAt = trigger.Type == TriggerType.Scheduled && !string.IsNullOrWhiteSpace(trigger.CronExpression)
            ? CronExpression.Parse(trigger.CronExpression).GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc)
            : null;
        var webhookUrl = trigger.Type == TriggerType.Webhook && tenantSlug is not null
            ? $"/webhooks/{tenantSlug}/{integration.Slug}/{trigger.Slug}"
            : null;

        return new PackageProvisionedTriggerResult(
            trigger.Id,
            trigger.Name,
            trigger.Slug,
            trigger.Type.ToString(),
            trigger.Enabled,
            upsert.Created ? "Created" : "Updated",
            trigger.CronExpression,
            nextRunAt,
            webhookUrl,
            upsert.WebhookSecretPreserved,
            trigger.DeclaredCronExpression,
            upsert.CronOverridden,
            upsert.EnabledOverridden);
    }

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
