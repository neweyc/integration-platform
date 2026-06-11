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

    // For message (Queue) triggers: the subject this integration subscribes to.
    public string? Subject { get; set; }

    // The cron/subject/enabled state the code last declared. The active CronExpression/Subject/Enabled
    // above may diverge when an operator overrides them in the control plane; that divergence is treated
    // as an operator override that package redeploys preserve (recording the new code default here and
    // reporting the difference as drift).
    public string? DeclaredCronExpression { get; set; }
    public string? DeclaredSubject { get; set; }
    public bool DeclaredEnabled { get; set; } = true;

    // For webhook triggers. The secret is shown once at creation and stored encrypted.
    public string? EncryptedWebhookSecret { get; set; }
}
