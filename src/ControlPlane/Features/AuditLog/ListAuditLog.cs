using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AuditLog;

public record ListAuditLogCommand(Guid TenantId, int Limit) : ICommand<ListAuditLogResult>;

public record ListAuditLogResult(IReadOnlyList<AuditLogItem> Entries);

public record AuditLogItem(
    Guid Id,
    Guid? ActorUserId,
    string ActorEmail,
    string Action,
    string TargetType,
    string? TargetId,
    string? Summary,
    DateTime OccurredAt);

public interface IAuditLogReadRepository
{
    Task<IReadOnlyList<AuditLogEntry>> ListAsync(Guid tenantId, int limit, CancellationToken ct = default);
}

public class AuditLogReadRepository(AppDbContext db) : IAuditLogReadRepository
{
    public async Task<IReadOnlyList<AuditLogEntry>> ListAsync(Guid tenantId, int limit, CancellationToken ct = default) =>
        await db.AuditLog
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.OccurredAt)
            .Take(limit)
            .ToListAsync(ct);
}

public class ListAuditLogHandler(IAuditLogReadRepository repository)
    : ICommandHandler<ListAuditLogCommand, ListAuditLogResult>
{
    private const int MaxLimit = 200;

    public async Task<ListAuditLogResult> HandleAsync(ListAuditLogCommand command, CancellationToken ct = default)
    {
        var limit = Math.Clamp(command.Limit, 1, MaxLimit);
        var entries = await repository.ListAsync(command.TenantId, limit, ct);

        return new ListAuditLogResult(entries.Select(a => new AuditLogItem(
            a.Id, a.ActorUserId, a.ActorEmail, a.Action.ToString(),
            a.TargetType, a.TargetId, a.Summary, a.OccurredAt)).ToList());
    }
}
