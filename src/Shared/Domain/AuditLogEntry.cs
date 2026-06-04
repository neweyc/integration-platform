namespace Shared.Domain;

/// <summary>
/// An immutable record of a security- or configuration-relevant action. Never stores
/// secret values — only the identity of what changed.
/// </summary>
public class AuditLogEntry : Entity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    // The user who performed the action. Null for system/self-service flows where no
    // authenticated user exists; ActorEmail is denormalized so the record survives user deletion.
    public Guid? ActorUserId { get; set; }
    public string ActorEmail { get; set; } = string.Empty;

    public AuditAction Action { get; set; }

    // What was acted upon, e.g. "Secret", "Integration", "AgentToken".
    public string TargetType { get; set; } = string.Empty;

    // Identifier of the target (a GUID, or a composite key like "production/API_KEY").
    public string? TargetId { get; set; }

    // Human-readable, value-free summary. Must never contain secret values.
    public string? Summary { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public enum AuditAction
{
    SecretSet,
    SecretDeleted,
    IntegrationCreated,
    IntegrationUpdated,
    IntegrationDeleted,
    AgentTokenCreated,
    AgentTokenRevoked,
    UserTokenCreated,
    UserTokenRevoked,
    PackageUploaded,
    PackageDeleted,
    UserInvited,
    InvitationAccepted,
    InvitationRevoked,
    InvitationResent
}
