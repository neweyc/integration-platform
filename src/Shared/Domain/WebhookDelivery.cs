namespace Shared.Domain;

/// <summary>
/// An append-only record of every inbound webhook delivery attempt against a known
/// webhook integration, successful or not. Gives operators visibility into senders,
/// signature failures, replays, and duplicates.
/// </summary>
public class WebhookDelivery : Entity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public Guid IntegrationId { get; set; }
    public Integration Integration { get; set; } = null!;

    // The sender-supplied delivery id (untrusted), if any.
    public string? DeliveryId { get; set; }

    public WebhookDeliveryOutcome Outcome { get; set; }

    // Set only when the delivery was accepted and a work item was enqueued.
    public Guid? WorkItemId { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}

public enum WebhookDeliveryOutcome
{
    Accepted,
    Deduplicated,
    InvalidSignature,
    Expired
}
