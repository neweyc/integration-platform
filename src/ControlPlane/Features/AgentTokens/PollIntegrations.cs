using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

// Claims due scheduled integrations for an environment, acquiring a lease to prevent duplicate dispatch.
public record PollIntegrationsCommand(
    Guid TenantId,
    string Environment,
    Guid LeaseOwnerId) : ICommand<PollIntegrationsResult>;

public record PollIntegrationsResult(IReadOnlyList<AgentIntegrationItem> Integrations);

public record AgentIntegrationItem(
    Guid Id,
    string Name,
    string Slug,
    TriggerType TriggerType,
    string? CronExpression,
    string ClassName,
    DateTime? LeaseExpiresAt);

public interface IPollRepository
{
    Task<IReadOnlyList<ClaimedIntegration>> ClaimDueScheduledAsync(
        Guid tenantId,
        string environment,
        Guid leaseOwnerId,
        TimeSpan leaseDuration,
        DateTime now,
        CancellationToken ct = default);
}

public record ClaimedIntegration(Integration Integration, DateTime LeaseExpiresAt);

public class PollIntegrationsHandler(IPollRepository repository)
    : ICommandHandler<PollIntegrationsCommand, PollIntegrationsResult>
{
    // Leases expire after 5 minutes by default. If an agent crashes, work can be reclaimed.
    private static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromMinutes(5);

    public async Task<PollIntegrationsResult> HandleAsync(PollIntegrationsCommand command, CancellationToken ct = default)
    {
        var claimed = await repository.ClaimDueScheduledAsync(
            command.TenantId,
            command.Environment,
            command.LeaseOwnerId,
            DefaultLeaseDuration,
            DateTime.UtcNow,
            ct);

        var items = claimed
            .Select(c => new AgentIntegrationItem(
                c.Integration.Id,
                c.Integration.Name,
                c.Integration.Slug,
                c.Integration.TriggerType,
                c.Integration.CronExpression,
                c.Integration.ClassName,
                c.LeaseExpiresAt))
            .ToList();

        return new PollIntegrationsResult(items);
    }
}
