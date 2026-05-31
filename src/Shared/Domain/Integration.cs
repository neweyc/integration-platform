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
