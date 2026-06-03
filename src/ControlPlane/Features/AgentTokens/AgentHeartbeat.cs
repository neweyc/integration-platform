using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public record AgentHeartbeatCommand(
    Guid TenantId,
    Guid AgentTokenId,
    string Environment,
    string? Version,
    string? Hostname,
    int CurrentConcurrency,
    int MaxConcurrency) : ICommand<bool>;

public record ListAgentHeartbeatsCommand(Guid TenantId) : ICommand<ListAgentHeartbeatsResult>;

public record ListAgentHeartbeatsResult(IReadOnlyList<AgentHeartbeatItem> Agents);

public record AgentHeartbeatItem(
    Guid Id,
    Guid AgentTokenId,
    string Environment,
    string? Version,
    string? Hostname,
    int CurrentConcurrency,
    int MaxConcurrency,
    DateTime LastSeenAt,
    bool IsStale);

public interface IAgentHeartbeatRepository
{
    Task UpsertAsync(AgentHeartbeat heartbeat, CancellationToken ct = default);
    Task<IReadOnlyList<AgentHeartbeat>> ListAsync(Guid tenantId, CancellationToken ct = default);
}

public class AgentHeartbeatHandler(IAgentHeartbeatRepository repository)
    : ICommandHandler<AgentHeartbeatCommand, bool>
{
    public async Task<bool> HandleAsync(AgentHeartbeatCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Environment))
            throw new ValidationException("Environment is required.");

        if (command.CurrentConcurrency < 0)
            throw new ValidationException("Current concurrency cannot be negative.");

        if (command.MaxConcurrency < 0)
            throw new ValidationException("Max concurrency cannot be negative.");

        await repository.UpsertAsync(new AgentHeartbeat
        {
            TenantId = command.TenantId,
            AgentTokenId = command.AgentTokenId,
            Environment = command.Environment,
            Version = string.IsNullOrWhiteSpace(command.Version) ? null : command.Version,
            Hostname = string.IsNullOrWhiteSpace(command.Hostname) ? null : command.Hostname,
            CurrentConcurrency = command.CurrentConcurrency,
            MaxConcurrency = command.MaxConcurrency,
            LastSeenAt = DateTime.UtcNow
        }, ct);

        return true;
    }
}

public class ListAgentHeartbeatsHandler(IAgentHeartbeatRepository repository)
    : ICommandHandler<ListAgentHeartbeatsCommand, ListAgentHeartbeatsResult>
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    public async Task<ListAgentHeartbeatsResult> HandleAsync(
        ListAgentHeartbeatsCommand command,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var agents = await repository.ListAsync(command.TenantId, ct);

        return new ListAgentHeartbeatsResult(agents
            .OrderByDescending(a => a.LastSeenAt)
            .Select(a => new AgentHeartbeatItem(
                a.Id,
                a.AgentTokenId,
                a.Environment,
                a.Version,
                a.Hostname,
                a.CurrentConcurrency,
                a.MaxConcurrency,
                a.LastSeenAt,
                now - a.LastSeenAt > StaleAfter))
            .ToList());
    }
}

public class AgentHeartbeatRepository(AppDbContext db) : IAgentHeartbeatRepository
{
    public async Task UpsertAsync(AgentHeartbeat heartbeat, CancellationToken ct = default)
    {
        var existing = await db.AgentHeartbeats
            .FirstOrDefaultAsync(h => h.TenantId == heartbeat.TenantId
                                   && h.AgentTokenId == heartbeat.AgentTokenId, ct);

        if (existing is null)
        {
            db.AgentHeartbeats.Add(heartbeat);
        }
        else
        {
            existing.Environment = heartbeat.Environment;
            existing.Version = heartbeat.Version;
            existing.Hostname = heartbeat.Hostname;
            existing.CurrentConcurrency = heartbeat.CurrentConcurrency;
            existing.MaxConcurrency = heartbeat.MaxConcurrency;
            existing.LastSeenAt = heartbeat.LastSeenAt;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AgentHeartbeat>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await db.AgentHeartbeats
            .Where(h => h.TenantId == tenantId)
            .OrderByDescending(h => h.LastSeenAt)
            .ToListAsync(ct);
    }
}
