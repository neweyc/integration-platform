namespace Shared.Domain;

// Tenant-wide alert configuration: the shared SMTP server plus the default alert destinations every
// integration inherits unless it overrides them. One row per tenant. Each channel is independently
// optional, so a tenant can alert by email, by webhook, by both, or by neither.
public class TenantAlertSettings : Entity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    // Email channel — the tenant-default recipients. Relays through the SMTP server configured below.
    public bool EmailEnabled { get; set; }
    public string? EmailRecipients { get; set; } // comma-separated addresses

    // SMTP server used for ALL email alerts, both the tenant default recipients and any per-integration
    // custom recipients. Set once at the tenant level so credentials are not re-entered per integration.
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseStartTls { get; set; } = true;
    public string? SmtpUsername { get; set; }
    public string? SmtpEncryptedPassword { get; set; }
    public string? SmtpFromAddress { get; set; }
    public string? SmtpFromName { get; set; }

    // Outbound webhook channel — the tenant-default destination. The optional secret signs the payload
    // (HMAC-SHA256) so the receiver can verify it came from us.
    public bool WebhookEnabled { get; set; }
    public string? WebhookUrl { get; set; }
    public string? WebhookEncryptedSecret { get; set; }
}

// Per-integration override of the tenant alert defaults. The absence of a row means Inherit.
public class IntegrationAlertSettings : Entity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid IntegrationId { get; set; }
    public Integration Integration { get; set; } = null!;

    public AlertMode Mode { get; set; } = AlertMode.Inherit;

    // Used only when Mode == Custom. Email still relays through the tenant SMTP server; only the
    // recipients and the webhook destination are integration-specific.
    public bool EmailEnabled { get; set; }
    public string? EmailRecipients { get; set; }
    public bool WebhookEnabled { get; set; }
    public string? WebhookUrl { get; set; }
    public string? WebhookEncryptedSecret { get; set; }
}

public enum AlertMode
{
    // Use the tenant-default destinations.
    Inherit,

    // Suppress all alerts for this integration.
    Off,

    // Use this integration's own destinations instead of the tenant defaults.
    Custom
}
