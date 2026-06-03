namespace Shared.Domain;

public class WorkItem : Entity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public Guid IntegrationId { get; set; }
    public Integration Integration { get; set; } = null!;

    public string Environment { get; set; } = string.Empty;
    public TriggerSource TriggerSource { get; set; }
    public WorkItemStatus Status { get; set; } = WorkItemStatus.Pending;

    // When the work item becomes eligible for claiming (immediately for manual, cron-based for scheduled)
    public DateTime AvailableAt { get; set; }

    // Agent token ID that has claimed this item
    public Guid? ClaimOwner { get; set; }
    public DateTime? ClaimExpiresAt { get; set; }

    // For Manual trigger: links back to the originating ManualRunRequest
    public Guid? ManualRunRequestId { get; set; }

    // Request body for Webhook triggers; null for Scheduled and Manual
    public string? Payload { get; set; }

    // Webhook delivery ID for idempotency. Prevents double-processing if the sender retries.
    public string? DeliveryId { get; set; }

    public bool HasActiveClaim(DateTime now) =>
        ClaimOwner.HasValue && ClaimExpiresAt.HasValue && ClaimExpiresAt.Value > now;

    public bool IsClaimExpired(DateTime now) =>
        ClaimOwner.HasValue && ClaimExpiresAt.HasValue && ClaimExpiresAt.Value <= now;

    public bool IsClaimOwnedBy(Guid ownerId, DateTime now) =>
        ClaimOwner == ownerId && HasActiveClaim(now);
}

public enum WorkItemStatus
{
    Pending,
    Claimed,
    Started,
    Completed,
    Failed,
    TimedOut
}
