using Shared.Domain;

namespace ControlPlane.Infrastructure.Auditing;

/// <summary>
/// Implemented by commands that should produce an audit entry. The dispatcher records the
/// returned descriptor after the command succeeds. Implementing this is additive — it does
/// not change a command's constructor or call sites.
/// </summary>
public interface IAuditableCommand
{
    /// <summary>
    /// Describe the audit entry for this command given its result, or null to skip auditing.
    /// Never include secret values in the summary.
    /// </summary>
    AuditDescriptor? Describe(object? result);
}

/// <summary>
/// Describes a single audit entry. Actor and tenant are taken from the current user unless
/// explicit values are supplied (for self-service flows like accepting an invitation, where
/// there is no authenticated user yet).
/// </summary>
public record AuditDescriptor(
    AuditAction Action,
    string TargetType,
    string? TargetId,
    string? Summary,
    Guid? ExplicitTenantId = null,
    Guid? ExplicitActorUserId = null,
    string? ExplicitActorEmail = null);
