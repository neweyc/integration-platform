namespace Shared.Domain;

public class Integration : Entity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Environment { get; set; } = string.Empty;
    public IntegrationStatus Status { get; set; } = IntegrationStatus.Enabled;
    public TriggerType TriggerType { get; set; }

    // For Scheduled triggers this holds the cron expression (e.g. "0 */6 * * *").
    // Null for Webhook and Manual triggers.
    public string? CronExpression { get; set; }

    // Fully qualified class name that implements IIntegration (e.g. "MyCompany.Integrations.SyncOrdersIntegration").
    // The runtime agent uses this to locate and instantiate the integration class.
    public string ClassName { get; set; } = string.Empty;

    // Maximum seconds an execution may run before being cancelled. Null means no timeout.
    public int? TimeoutSeconds { get; set; }

    // Number of retry attempts after the initial attempt. Zero disables retries.
    public int RetryMaxAttempts { get; set; }

    // Delay before the next retry attempt. Null means retry immediately.
    public int? RetryBackoffSeconds { get; set; }

    // Pinned package. Null means the agent resolves from its local IntegrationsPath in dev mode.
    public Guid? PackageId { get; set; }

    // AES-encrypted webhook secret. Only set for TriggerType=Webhook.
    public string? EncryptedWebhookSecret { get; set; }

    public Tenant Tenant { get; set; } = null!;
}

public enum IntegrationStatus
{
    Enabled,
    Disabled
}

public enum TriggerType
{
    Scheduled,
    Webhook,
    Manual
}
