namespace Shared.Domain;

/// <summary>
/// Generic append-only observability record for trigger adapters.
/// Captures what an adapter received, accepted, rejected, deduplicated, or converted to work.
/// </summary>
public class TriggerEvent : Entity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public Guid IntegrationId { get; set; }
    public Integration Integration { get; set; } = null!;

    public Guid? IntegrationTriggerId { get; set; }
    public IntegrationTrigger? IntegrationTrigger { get; set; }

    public string AdapterKey { get; set; } = string.Empty;
    public TriggerSource Source { get; set; }

    // Adapter-specific event/delivery/dedupe key, if supplied by the source system.
    public string? EventKey { get; set; }

    public TriggerEventOutcome Outcome { get; set; }

    public Guid? WorkItemId { get; set; }

    // Small adapter-defined metadata JSON. Full payloads stay on WorkItem.Payload where needed.
    public string? MetadataJson { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}

public enum TriggerEventOutcome
{
    Received,
    Accepted,
    Deduplicated,
    Rejected,
    ConvertedToWork,
    Failed
}
