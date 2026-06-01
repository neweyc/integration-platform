namespace Shared.Domain;

/// <summary>
/// Represents a user request to run an integration manually.
/// Agents poll for pending requests and claim them for execution.
/// </summary>
public class ManualRunRequest : Entity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public Guid IntegrationId { get; set; }
    public Integration Integration { get; set; } = null!;

    public string Environment { get; set; } = string.Empty;
    public ManualRunStatus Status { get; set; } = ManualRunStatus.Pending;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The agent token that claimed this request for execution.
    /// </summary>
    public Guid? ClaimedByAgentTokenId { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime? ClaimExpiresAt { get; set; }

    /// <summary>
    /// The execution record created when this run started.
    /// </summary>
    public Guid? ExecutionRecordId { get; set; }

    /// <summary>
    /// Returns true if the claim is active (not expired and owned by an agent).
    /// </summary>
    public bool HasActiveClaim(DateTime now) =>
        Status == ManualRunStatus.Claimed &&
        ClaimExpiresAt.HasValue &&
        ClaimExpiresAt.Value > now;

    /// <summary>
    /// Returns true if the claim has expired and can be reclaimed.
    /// </summary>
    public bool IsClaimExpired(DateTime now) =>
        Status == ManualRunStatus.Claimed &&
        ClaimExpiresAt.HasValue &&
        ClaimExpiresAt.Value <= now;
}

public enum ManualRunStatus
{
    /// <summary>
    /// Waiting for an agent to pick up.
    /// </summary>
    Pending,

    /// <summary>
    /// An agent has claimed this and is starting execution.
    /// </summary>
    Claimed,

    /// <summary>
    /// Execution has started (ExecutionRecordId is set).
    /// </summary>
    Started,

    /// <summary>
    /// The request expired before being picked up.
    /// </summary>
    Expired
}
