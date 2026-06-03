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
    Guid? ManualRunRequestId,
    int? TimeoutSeconds = null,
    Guid? WorkItemId = null,
    Guid? PackageId = null);

public interface IPollRepository
{
    Task<IReadOnlyList<ClaimedWork>> ClaimDueScheduledAsync(
        Guid tenantId,
        string environment,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default);

    Task<IReadOnlyList<ClaimedWork>> ClaimPendingManualRunsAsync(
        Guid tenantId,
        string environment,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default);
}

public record ClaimedWork(Integration Integration, WorkItem WorkItem);

public class PollIntegrationsHandler(IPollRepository repository)
    : ICommandHandler<PollIntegrationsCommand, PollIntegrationsResult>
{
    private static readonly TimeSpan DefaultClaimDuration = TimeSpan.FromMinutes(5);

    public async Task<PollIntegrationsResult> HandleAsync(PollIntegrationsCommand command, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var scheduled = await repository.ClaimDueScheduledAsync(
            command.TenantId, command.Environment, command.LeaseOwnerId, DefaultClaimDuration, now, ct);

        var manualRuns = await repository.ClaimPendingManualRunsAsync(
            command.TenantId, command.Environment, command.LeaseOwnerId, DefaultClaimDuration, now, ct);

        var items = new List<AgentIntegrationItem>();

        foreach (var c in scheduled)
        {
            items.Add(new AgentIntegrationItem(
                c.Integration.Id,
                c.Integration.Name,
                c.Integration.Slug,
                c.Integration.TriggerType,
                c.Integration.CronExpression,
                c.Integration.ClassName,
                c.WorkItem.ClaimExpiresAt,
                TriggerSource.Scheduled,
                null,
                c.Integration.TimeoutSeconds,
                c.WorkItem.Id,
                c.Integration.PackageId));
        }

        foreach (var m in manualRuns)
        {
            items.Add(new AgentIntegrationItem(
                m.Integration.Id,
                m.Integration.Name,
                m.Integration.Slug,
                m.Integration.TriggerType,
                m.Integration.CronExpression,
                m.Integration.ClassName,
                m.WorkItem.ClaimExpiresAt,
                TriggerSource.Manual,
                m.WorkItem.ManualRunRequestId,
                m.Integration.TimeoutSeconds,
                m.WorkItem.Id,
                m.Integration.PackageId));
        }

        return new PollIntegrationsResult(items);
    }
}
