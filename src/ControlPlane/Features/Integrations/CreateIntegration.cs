using System.Security.Cryptography;
using ControlPlane.Features.Webhooks;
using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Cronos;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

public record IntegrationTriggerInput(
    string Name,
    string Slug,
    TriggerType Type,
    bool Enabled = true,
    string? CronExpression = null);

public record CreateIntegrationCommand(
    Guid TenantId,
    string Name,
    string Slug,
    string? Description,
    string Environment,
    string ClassName,
    IReadOnlyList<IntegrationTriggerInput> Triggers,
    int? TimeoutSeconds = null,
    int RetryMaxAttempts = 0,
    int? RetryBackoffSeconds = null,
    Guid? PackageId = null) : ICommand<CreateIntegrationResult>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        new(AuditAction.IntegrationCreated, "Integration",
            (result as CreateIntegrationResult)?.Id.ToString(), $"Created integration '{Slug}'");
}

public record CreateIntegrationResult(
    Guid Id,
    string Name,
    string Slug,
    string Environment,
    string Status,
    string ClassName,
    IReadOnlyList<IntegrationTriggerResult> Triggers,
    int? TimeoutSeconds = null,
    int RetryMaxAttempts = 0,
    int? RetryBackoffSeconds = null,
    Guid? PackageId = null);

public record IntegrationTriggerResult(
    Guid Id,
    string Name,
    string Slug,
    string Type,
    bool Enabled,
    string? CronExpression = null,
    string? WebhookUrl = null,
    string? WebhookSecret = null,
    WebhookSigning? WebhookSigning = null,
    string? DeclaredCronExpression = null,
    bool CronOverridden = false,
    bool EnabledOverridden = false);

/// <summary>
/// Tells a webhook integrator exactly how to sign their requests.
/// </summary>
public record WebhookSigning(
    string Algorithm,          // "HMAC-SHA256"
    string SignatureHeader,    // "X-Integration-Signature"
    string SignatureFormat,    // "sha256=<lowercase hex HMAC of '{timestamp}.{body}'>"
    string DeliveryIdHeader,   // "X-Integration-Delivery"; optional, enables idempotent retries.
    string TimestampHeader,    // "X-Integration-Timestamp"; unix seconds, required for replay protection.
    int ToleranceSeconds);     // deliveries outside this window from now are rejected as replays.

public interface IIntegrationRepository
{
    Task<bool> SlugExistsAsync(Guid tenantId, string slug, CancellationToken ct = default);
    Task<bool> PackageExistsAsync(Guid tenantId, Guid packageId, CancellationToken ct = default);
    Task<string?> GetTenantSlugAsync(Guid tenantId, CancellationToken ct = default);
    Task<Integration> CreateAsync(Integration integration, IReadOnlyList<IntegrationTrigger> triggers, CancellationToken ct = default);
    Task<IntegrationUpsertResult> UpsertBySlugAsync(Integration integration, IReadOnlyList<IntegrationTrigger> triggers, CancellationToken ct = default);
}

public record IntegrationUpsertResult(
    Integration Integration,
    bool Created,
    IReadOnlyList<IntegrationTriggerUpsertResult> Triggers);

public record IntegrationTriggerUpsertResult(
    IntegrationTrigger Trigger,
    bool Created,
    bool WebhookSecretPreserved,
    bool CronOverridden = false,
    bool EnabledOverridden = false);

// Whether a trigger reconcile is driven by code (package upload, which records declared defaults and
// preserves operator overrides) or by an operator (UI/API update, whose active values become overrides
// when they diverge from the declared defaults).
public enum TriggerReconcileSource
{
    Operator,
    Code
}

public class CreateIntegrationHandler(IIntegrationRepository repository, IEncryptionService encryption)
    : ICommandHandler<CreateIntegrationCommand, CreateIntegrationResult>
{
    public async Task<CreateIntegrationResult> HandleAsync(CreateIntegrationCommand command, CancellationToken ct = default)
    {
        ValidateCommand(command);

        if (await repository.SlugExistsAsync(command.TenantId, command.Slug, ct))
            throw new ConflictException($"An integration with slug '{command.Slug}' already exists.");

        if (command.PackageId.HasValue
            && !await repository.PackageExistsAsync(command.TenantId, command.PackageId.Value, ct))
            throw new NotFoundException($"Package '{command.PackageId}' not found.");

        var oneTimeSecrets = new Dictionary<string, string>();
        var triggers = BuildTriggers(command.TenantId, command.Triggers, encryption, oneTimeSecrets);

        var integration = new Integration
        {
            TenantId = command.TenantId,
            Name = command.Name,
            Slug = command.Slug,
            Description = command.Description,
            Environment = command.Environment,
            ClassName = command.ClassName,
            TimeoutSeconds = command.TimeoutSeconds,
            RetryMaxAttempts = command.RetryMaxAttempts,
            RetryBackoffSeconds = command.RetryBackoffSeconds,
            PackageId = command.PackageId,
            Status = IntegrationStatus.Enabled
        };

        var created = await repository.CreateAsync(integration, triggers, ct);
        var tenantSlug = await repository.GetTenantSlugAsync(command.TenantId, ct);

        return ToResult(created, tenantSlug, oneTimeSecrets);
    }

    internal static List<IntegrationTrigger> BuildTriggers(
        Guid tenantId,
        IReadOnlyList<IntegrationTriggerInput> inputs,
        IEncryptionService encryption,
        Dictionary<string, string>? oneTimeSecrets = null)
    {
        var triggers = new List<IntegrationTrigger>();

        foreach (var input in inputs)
        {
            string? encryptedWebhookSecret = null;
            if (input.Type == TriggerType.Webhook)
            {
                var secretBytes = RandomNumberGenerator.GetBytes(32);
                var plainSecret = "whs_" + Convert.ToHexString(secretBytes).ToLowerInvariant();
                encryptedWebhookSecret = encryption.Encrypt(plainSecret);
                oneTimeSecrets?.Add(input.Slug, plainSecret);
            }

            var cronExpression = input.Type == TriggerType.Scheduled ? input.CronExpression : null;
            triggers.Add(new IntegrationTrigger
            {
                TenantId = tenantId,
                Name = input.Name,
                Slug = input.Slug,
                Type = input.Type,
                Enabled = input.Enabled,
                CronExpression = cronExpression,
                // A freshly built trigger has no override yet: the declared defaults match the active values.
                DeclaredCronExpression = cronExpression,
                DeclaredEnabled = input.Enabled,
                EncryptedWebhookSecret = encryptedWebhookSecret
            });
        }

        return triggers;
    }

    internal static void ValidateCommand(CreateIntegrationCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ValidationException("Name is required.");

        if (string.IsNullOrWhiteSpace(command.Slug))
            throw new ValidationException("Slug is required.");

        ValidateSlug(command.Slug, "Slug");

        if (string.IsNullOrWhiteSpace(command.Environment))
            throw new ValidationException("Environment is required.");

        if (string.IsNullOrWhiteSpace(command.ClassName))
            throw new ValidationException("Class name is required.");

        if (!System.Text.RegularExpressions.Regex.IsMatch(command.ClassName, @"^[\w]+(?:\.[\w]+)*$"))
            throw new ValidationException("Class name must be a valid fully-qualified .NET type name (e.g. 'MyCompany.Integrations.SyncOrdersIntegration').");

        if (command.TimeoutSeconds is <= 0)
            throw new ValidationException("Timeout must be greater than zero seconds.");

        if (command.RetryMaxAttempts < 0)
            throw new ValidationException("Retry max attempts cannot be negative.");

        if (command.RetryBackoffSeconds is < 0)
            throw new ValidationException("Retry backoff cannot be negative.");

        ValidateTriggers(command.Triggers);
    }

    internal static void ValidateTriggers(IReadOnlyList<IntegrationTriggerInput> triggers)
    {
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var trigger in triggers)
        {
            if (string.IsNullOrWhiteSpace(trigger.Name))
                throw new ValidationException("Trigger name is required.");

            if (string.IsNullOrWhiteSpace(trigger.Slug))
                throw new ValidationException("Trigger slug is required.");

            ValidateSlug(trigger.Slug, "Trigger slug");

            if (!slugs.Add(trigger.Slug))
                throw new ValidationException($"Duplicate trigger slug '{trigger.Slug}'.");

            if (trigger.Type == TriggerType.Scheduled)
            {
                if (string.IsNullOrWhiteSpace(trigger.CronExpression))
                    throw new ValidationException("A cron expression is required for scheduled triggers.");

                if (!IsValidCronExpression(trigger.CronExpression))
                    throw new ValidationException($"'{trigger.CronExpression}' is not a valid cron expression.");
            }
        }
    }

    private static void ValidateSlug(string slug, string label)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[a-z0-9-]+$"))
            throw new ValidationException($"{label} may only contain lowercase letters, numbers, and hyphens.");
    }

    private static bool IsValidCronExpression(string expression)
    {
        try
        {
            CronExpression.Parse(expression);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static CreateIntegrationResult ToResult(
        Integration integration,
        string? tenantSlug = null,
        IReadOnlyDictionary<string, string>? oneTimeSecrets = null) =>
        new(
            integration.Id,
            integration.Name,
            integration.Slug,
            integration.Environment,
            integration.Status.ToString(),
            integration.ClassName,
            integration.Triggers.Select(t => ToTriggerResult(integration, t, tenantSlug, oneTimeSecrets)).ToList(),
            integration.TimeoutSeconds,
            integration.RetryMaxAttempts,
            integration.RetryBackoffSeconds,
            integration.PackageId);

    internal static IntegrationTriggerResult ToTriggerResult(
        Integration integration,
        IntegrationTrigger trigger,
        string? tenantSlug = null,
        IReadOnlyDictionary<string, string>? oneTimeSecrets = null)
    {
        var webhookUrl = trigger.Type == TriggerType.Webhook && tenantSlug is not null
            ? $"/webhooks/{tenantSlug}/{integration.Slug}/{trigger.Slug}"
            : null;
        var webhookSecret = oneTimeSecrets?.GetValueOrDefault(trigger.Slug);
        var signing = trigger.Type == TriggerType.Webhook
            ? new WebhookSigning(
                WebhookHeaders.Algorithm,
                WebhookHeaders.Signature,
                WebhookHeaders.SignatureFormat,
                WebhookHeaders.Delivery,
                WebhookHeaders.Timestamp,
                WebhookHeaders.ToleranceSeconds)
            : null;

        return new IntegrationTriggerResult(
            trigger.Id,
            trigger.Name,
            trigger.Slug,
            trigger.Type.ToString(),
            trigger.Enabled,
            trigger.CronExpression,
            webhookUrl,
            webhookSecret,
            signing,
            trigger.DeclaredCronExpression,
            CronOverridden: !string.Equals(trigger.CronExpression, trigger.DeclaredCronExpression, StringComparison.Ordinal),
            EnabledOverridden: trigger.Enabled != trigger.DeclaredEnabled);
    }
}
