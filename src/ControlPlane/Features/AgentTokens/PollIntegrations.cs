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
    Guid? PackageId = null,
    string? Payload = null);

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

    Task<IReadOnlyList<ClaimedWork>> ClaimPendingWebhookRunsAsync(
        Guid tenantId,
        string environment,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default);

    Task<IReadOnlyList<ClaimedWork>> ClaimPendingRetryRunsAsync(
        Guid tenantId,
        string environment,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default);

    Task<IReadOnlyList<ClaimedWork>> ClaimPendingWorkflowRunsAsync(
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

        var webhookRuns = await repository.ClaimPendingWebhookRunsAsync(
            command.TenantId, command.Environment, command.LeaseOwnerId, DefaultClaimDuration, now, ct);

        var retryRuns = await repository.ClaimPendingRetryRunsAsync(
            command.TenantId, command.Environment, command.LeaseOwnerId, DefaultClaimDuration, now, ct);

        var workflowRuns = await repository.ClaimPendingWorkflowRunsAsync(
            command.TenantId, command.Environment, command.LeaseOwnerId, DefaultClaimDuration, now, ct);

        var items = new List<AgentIntegrationItem>();

        foreach (var c in scheduled)
        {
            items.Add(new AgentIntegrationItem(
                c.Integration.Id,
                c.Integration.Name,
                c.Integration.Slug,
                TriggerTypeFor(c.WorkItem),
                c.WorkItem.IntegrationTrigger?.CronExpression,
                c.Integration.ClassName,
                c.WorkItem.ClaimExpiresAt,
                TriggerSource.Scheduled,
                null,
                c.Integration.TimeoutSeconds,
                c.WorkItem.Id,
                c.Integration.PackageId,
                c.WorkItem.Payload));
        }

        foreach (var m in manualRuns)
        {
            items.Add(new AgentIntegrationItem(
                m.Integration.Id,
                m.Integration.Name,
                m.Integration.Slug,
                TriggerType.Manual,
                null,
                m.Integration.ClassName,
                m.WorkItem.ClaimExpiresAt,
                TriggerSource.Manual,
                m.WorkItem.ManualRunRequestId,
                m.Integration.TimeoutSeconds,
                m.WorkItem.Id,
                m.Integration.PackageId,
                m.WorkItem.Payload));
        }

        foreach (var w in webhookRuns)
        {
            items.Add(new AgentIntegrationItem(
                w.Integration.Id,
                w.Integration.Name,
                w.Integration.Slug,
                TriggerType.Webhook,
                null,
                w.Integration.ClassName,
                w.WorkItem.ClaimExpiresAt,
                TriggerSource.Webhook,
                null,
                w.Integration.TimeoutSeconds,
                w.WorkItem.Id,
                w.Integration.PackageId,
                w.WorkItem.Payload));
        }

        foreach (var r in retryRuns)
        {
            items.Add(new AgentIntegrationItem(
                r.Integration.Id,
                r.Integration.Name,
                r.Integration.Slug,
                TriggerType.Manual,
                null,
                r.Integration.ClassName,
                r.WorkItem.ClaimExpiresAt,
                TriggerSource.Retry,
                r.WorkItem.ManualRunRequestId,
                r.Integration.TimeoutSeconds,
                r.WorkItem.Id,
                r.Integration.PackageId,
                r.WorkItem.Payload));
        }

        foreach (var w in workflowRuns)
        {
            items.Add(new AgentIntegrationItem(
                w.Integration.Id,
                w.Integration.Name,
                w.Integration.Slug,
                TriggerType.Manual,
                null,
                w.Integration.ClassName,
                w.WorkItem.ClaimExpiresAt,
                TriggerSource.Workflow,
                null,
                w.Integration.TimeoutSeconds,
                w.WorkItem.Id,
                w.Integration.PackageId,
                w.WorkItem.Payload));
        }

        return new PollIntegrationsResult(items);
    }

    private static TriggerType TriggerTypeFor(WorkItem workItem) =>
        workItem.IntegrationTrigger?.Type
        ?? (workItem.TriggerSource == TriggerSource.Webhook ? TriggerType.Webhook : TriggerType.Manual);
}
