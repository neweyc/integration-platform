namespace Shared.Domain;

public class IntegrationScheduleState : Entity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public Guid IntegrationId { get; set; }
    public Integration Integration { get; set; } = null!;

    public DateTime? LastDispatchedAt { get; set; }
    public DateTime? NextRunAt { get; set; }

    /// <summary>
    /// The agent token ID that holds the current lease, if any.
    /// Null when no lease is active.
    /// </summary>
    public Guid? LeaseOwnerId { get; set; }

    /// <summary>
    /// When the current lease expires. After this time, another agent can reclaim the work.
    /// </summary>
    public DateTime? LeaseExpiresAt { get; set; }

    /// <summary>
    /// Checks whether the lease is currently active (not expired).
    /// </summary>
    public bool HasActiveLease(DateTime now) =>
        LeaseOwnerId.HasValue && LeaseExpiresAt.HasValue && LeaseExpiresAt.Value > now;

    /// <summary>
    /// Checks whether this owner holds a valid lease.
    /// </summary>
    public bool IsLeaseOwnedBy(Guid ownerId, DateTime now) =>
        LeaseOwnerId == ownerId && HasActiveLease(now);
}
