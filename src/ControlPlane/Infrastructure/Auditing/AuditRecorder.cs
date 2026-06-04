using Shared.Domain;

namespace ControlPlane.Infrastructure.Auditing;

public interface IAuditRecorder
{
    Task RecordAsync(AuditDescriptor descriptor, CancellationToken ct = default);
}

public class AuditRecorder(
    AppDbContext db,
    ICurrentUser currentUser,
    ILogger<AuditRecorder> logger) : IAuditRecorder
{
    public async Task RecordAsync(AuditDescriptor descriptor, CancellationToken ct = default)
    {
        try
        {
            // Either fully explicit (self-service flows with no authenticated user) or
            // fully from the current user (admin-initiated actions).
            Guid tenantId;
            Guid? actorUserId;
            string actorEmail;

            if (descriptor.ExplicitTenantId.HasValue)
            {
                tenantId = descriptor.ExplicitTenantId.Value;
                actorUserId = descriptor.ExplicitActorUserId;
                actorEmail = descriptor.ExplicitActorEmail ?? "unknown";
            }
            else
            {
                tenantId = currentUser.TenantId;
                actorUserId = currentUser.UserId;
                actorEmail = currentUser.Email;
            }

            db.AuditLog.Add(new AuditLogEntry
            {
                TenantId = tenantId,
                ActorUserId = actorUserId,
                ActorEmail = actorEmail,
                Action = descriptor.Action,
                TargetType = descriptor.TargetType,
                TargetId = descriptor.TargetId,
                Summary = descriptor.Summary,
                OccurredAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Auditing must never break the primary operation it records. Log and move on.
            logger.LogError(ex, "Failed to write audit entry for {Action} on {TargetType}",
                descriptor.Action, descriptor.TargetType);
        }
    }
}
