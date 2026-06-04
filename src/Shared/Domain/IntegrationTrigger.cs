namespace Shared.Domain;

public class IntegrationTrigger : Entity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public Guid IntegrationId { get; set; }
    public Integration Integration { get; set; } = null!;

    public TriggerType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    // For scheduled triggers.
    public string? CronExpression { get; set; }

    // For webhook triggers. The secret is shown once at creation and stored encrypted.
    public string? EncryptedWebhookSecret { get; set; }
}
