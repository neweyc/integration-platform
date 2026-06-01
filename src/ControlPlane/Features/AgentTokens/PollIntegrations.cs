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
    DateTime? LeaseExpiresAt,
    TriggerSource TriggerSource,
    Guid? ManualRunRequestId);

public interface IPollRepository
{
    Task<IReadOnlyList<ClaimedIntegration>> ClaimDueScheduledAsync(
        Guid tenantId,
        string environment,
        Guid leaseOwnerId,
        TimeSpan leaseDuration,
        DateTime now,
        CancellationToken ct = default);

    Task<IReadOnlyList<ClaimedManualRun>> ClaimPendingManualRunsAsync(
        Guid tenantId,
        string environment,
        Guid agentTokenId,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default);
}

public record ClaimedIntegration(Integration Integration, DateTime LeaseExpiresAt);
public record ClaimedManualRun(ManualRunRequest Request, Integration Integration);

public class PollIntegrationsHandler(IPollRepository repository)
    : ICommandHandler<PollIntegrationsCommand, PollIntegrationsResult>
{
    // Leases expire after 5 minutes by default. If an agent crashes, work can be reclaimed.
    private static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromMinutes(5);

    public async Task<PollIntegrationsResult> HandleAsync(PollIntegrationsCommand command, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Claim due scheduled integrations
        var scheduled = await repository.ClaimDueScheduledAsync(
            command.TenantId,
            command.Environment,
            command.LeaseOwnerId,
            DefaultLeaseDuration,
            now,
            ct);

        // Claim pending manual run requests (uses same lease duration)
        var manualRuns = await repository.ClaimPendingManualRunsAsync(
            command.TenantId,
            command.Environment,
            command.LeaseOwnerId,
            DefaultLeaseDuration,
            now,
            ct);

        var items = new List<AgentIntegrationItem>();

        // Add scheduled integrations
        foreach (var c in scheduled)
        {
            items.Add(new AgentIntegrationItem(
                c.Integration.Id,
                c.Integration.Name,
                c.Integration.Slug,
                c.Integration.TriggerType,
                c.Integration.CronExpression,
                c.Integration.ClassName,
                c.LeaseExpiresAt,
                TriggerSource.Scheduled,
                null));
        }

        // Add manual runs
        foreach (var m in manualRuns)
        {
            items.Add(new AgentIntegrationItem(
                m.Integration.Id,
                m.Integration.Name,
                m.Integration.Slug,
                m.Integration.TriggerType,
                m.Integration.CronExpression,
                m.Integration.ClassName,
                m.Request.ClaimExpiresAt, // Manual runs also have claim expiry
                TriggerSource.Manual,
                m.Request.Id));
        }

        return new PollIntegrationsResult(items);
    }
}
