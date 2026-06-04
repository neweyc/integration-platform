using System.Security.Cryptography;
using ControlPlane.Features.Webhooks;
using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Cronos;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

public record CreateIntegrationCommand(
    Guid TenantId,
    string Name,
    string Slug,
    string? Description,
    string Environment,
    TriggerType TriggerType,
    string? CronExpression,
    string ClassName,
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
    string TriggerType,
    string? CronExpression,
    string ClassName,
    int? TimeoutSeconds = null,
    int RetryMaxAttempts = 0,
    int? RetryBackoffSeconds = null,
    Guid? PackageId = null,
    // Webhook integrations only. The secret is shown once; the URL is the stable delivery path.
    string? WebhookSecret = null,
    string? WebhookUrl = null,
    // Self-documenting signing instructions so integrators don't have to guess
    WebhookSigning? WebhookSigning = null);

/// <summary>
/// Tells a webhook integrator exactly how to sign their requests.
/// </summary>
public record WebhookSigning(
    string Algorithm,          // "HMAC-SHA256"
    string SignatureHeader,    // "X-Integration-Signature"
    string SignatureFormat,    // "sha256=<lowercase hex digest of the raw request body>"
    string DeliveryIdHeader);  // "X-Integration-Delivery"; optional, enables idempotent retries.

public interface IIntegrationRepository
{
    Task<bool> SlugExistsAsync(Guid tenantId, string slug, CancellationToken ct = default);
    Task<bool> PackageExistsAsync(Guid tenantId, Guid packageId, CancellationToken ct = default);
    Task<string?> GetTenantSlugAsync(Guid tenantId, CancellationToken ct = default);
    Task<Integration> CreateAsync(Integration integration, CancellationToken ct = default);
    Task<Integration> UpsertBySlugAsync(Integration integration, CancellationToken ct = default);
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

        // Generate a webhook secret for Webhook integrations. It is shown once and never retrievable again.
        string? plainWebhookSecret = null;
        string? encryptedWebhookSecret = null;
        if (command.TriggerType == TriggerType.Webhook)
        {
            var secretBytes = RandomNumberGenerator.GetBytes(32);
            plainWebhookSecret = "whs_" + Convert.ToHexString(secretBytes).ToLowerInvariant();
            encryptedWebhookSecret = encryption.Encrypt(plainWebhookSecret);
        }

        var integration = new Integration
        {
            TenantId = command.TenantId,
            Name = command.Name,
            Slug = command.Slug,
            Description = command.Description,
            Environment = command.Environment,
            TriggerType = command.TriggerType,
            CronExpression = command.CronExpression,
            ClassName = command.ClassName,
            TimeoutSeconds = command.TimeoutSeconds,
            RetryMaxAttempts = command.RetryMaxAttempts,
            RetryBackoffSeconds = command.RetryBackoffSeconds,
            PackageId = command.PackageId,
            EncryptedWebhookSecret = encryptedWebhookSecret,
            Status = IntegrationStatus.Enabled
        };

        var created = await repository.CreateAsync(integration, ct);

        string? webhookUrl = null;
        WebhookSigning? signing = null;
        if (created.TriggerType == TriggerType.Webhook)
        {
            var tenantSlug = await repository.GetTenantSlugAsync(command.TenantId, ct);
            if (tenantSlug is not null)
                webhookUrl = $"/webhooks/{tenantSlug}/{created.Slug}";

            signing = new WebhookSigning(
                WebhookHeaders.Algorithm,
                WebhookHeaders.Signature,
                WebhookHeaders.SignatureFormat,
                WebhookHeaders.Delivery);
        }

        return ToResult(created, plainWebhookSecret, webhookUrl, signing);
    }

    private static void ValidateCommand(CreateIntegrationCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ValidationException("Name is required.");

        if (string.IsNullOrWhiteSpace(command.Slug))
            throw new ValidationException("Slug is required.");

        if (!System.Text.RegularExpressions.Regex.IsMatch(command.Slug, @"^[a-z0-9-]+$"))
            throw new ValidationException("Slug may only contain lowercase letters, numbers, and hyphens.");

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

        if (command.TriggerType == TriggerType.Scheduled)
        {
            if (string.IsNullOrWhiteSpace(command.CronExpression))
                throw new ValidationException("A cron expression is required for scheduled integrations.");

            if (!IsValidCronExpression(command.CronExpression))
                throw new ValidationException($"'{command.CronExpression}' is not a valid cron expression.");
        }
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
        Integration i,
        string? webhookSecret = null,
        string? webhookUrl = null,
        WebhookSigning? webhookSigning = null) =>
        new(i.Id, i.Name, i.Slug, i.Environment, i.Status.ToString(), i.TriggerType.ToString(),
            i.CronExpression, i.ClassName, i.TimeoutSeconds, i.RetryMaxAttempts, i.RetryBackoffSeconds, i.PackageId,
            webhookSecret, webhookUrl, webhookSigning);
}
