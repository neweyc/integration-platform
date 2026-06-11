using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

// Claims due scheduled integrations for an environment, acquiring a lease to prevent duplicate dispatch.
public record PollIntegrationsCommand(
    Guid TenantId,
    string Environment,
    Guid LeaseOwnerId,
    IReadOnlyList<string>? AgentTags = null) : ICommand<PollIntegrationsResult>;

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
    string? Payload = null,
    // Per-trigger-source metadata for context.Trigger — each populated only for its own source.
    string? MessageSubject = null,            // Queue
    Guid? MessageId = null,                   // Queue
    DateTime? MessagePublishedAt = null,      // Queue
    Guid? ParentExecutionId = null,           // Queue (message source) / Workflow (upstream)
    string? DeliveryId = null,                // Webhook
    Guid? WorkflowRunId = null,               // Workflow
    Guid? WorkflowNodeId = null,              // Workflow
    int AttemptNumber = 1);                   // Retry

// agentTags are the capabilities the polling agent offers; only work whose integration's required
// tags are a subset is claimable (see TagSet.IsSatisfiedBy).
public interface IPollRepository
{
    Task<IReadOnlyList<ClaimedWork>> ClaimDueScheduledAsync(
        Guid tenantId,
        string environment,
        IReadOnlyList<string> agentTags,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default);

    Task<IReadOnlyList<ClaimedWork>> ClaimPendingManualRunsAsync(
        Guid tenantId,
        string environment,
        IReadOnlyList<string> agentTags,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default);

    Task<IReadOnlyList<ClaimedWork>> ClaimPendingWebhookRunsAsync(
        Guid tenantId,
        string environment,
        IReadOnlyList<string> agentTags,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default);

    Task<IReadOnlyList<ClaimedWork>> ClaimPendingRetryRunsAsync(
        Guid tenantId,
        string environment,
        IReadOnlyList<string> agentTags,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default);

    Task<IReadOnlyList<ClaimedWork>> ClaimPendingWorkflowRunsAsync(
        Guid tenantId,
        string environment,
        IReadOnlyList<string> agentTags,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default);

    Task<IReadOnlyList<ClaimedWork>> ClaimPendingQueueRunsAsync(
        Guid tenantId,
        string environment,
        IReadOnlyList<string> agentTags,
        Guid claimOwner,
        TimeSpan claimDuration,
        DateTime now,
        CancellationToken ct = default);

    Task<IReadOnlyList<ClaimedWork>> ClaimPendingFileRunsAsync(
        Guid tenantId,
        string environment,
        IReadOnlyList<string> agentTags,
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
        var agentTags = command.AgentTags ?? [];

        var scheduled = await repository.ClaimDueScheduledAsync(
            command.TenantId, command.Environment, agentTags, command.LeaseOwnerId, DefaultClaimDuration, now, ct);

        var manualRuns = await repository.ClaimPendingManualRunsAsync(
            command.TenantId, command.Environment, agentTags, command.LeaseOwnerId, DefaultClaimDuration, now, ct);

        var webhookRuns = await repository.ClaimPendingWebhookRunsAsync(
            command.TenantId, command.Environment, agentTags, command.LeaseOwnerId, DefaultClaimDuration, now, ct);

        var retryRuns = await repository.ClaimPendingRetryRunsAsync(
            command.TenantId, command.Environment, agentTags, command.LeaseOwnerId, DefaultClaimDuration, now, ct);

        var workflowRuns = await repository.ClaimPendingWorkflowRunsAsync(
            command.TenantId, command.Environment, agentTags, command.LeaseOwnerId, DefaultClaimDuration, now, ct);

        var queueRuns = await repository.ClaimPendingQueueRunsAsync(
            command.TenantId, command.Environment, agentTags, command.LeaseOwnerId, DefaultClaimDuration, now, ct);

        var fileRuns = await repository.ClaimPendingFileRunsAsync(
            command.TenantId, command.Environment, agentTags, command.LeaseOwnerId, DefaultClaimDuration, now, ct);

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
                w.WorkItem.Payload,
                DeliveryId: w.WorkItem.DeliveryId));
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
                r.WorkItem.Payload,
                AttemptNumber: r.WorkItem.AttemptNumber));
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
                w.WorkItem.Payload,
                ParentExecutionId: w.WorkItem.ParentExecutionId,
                WorkflowRunId: w.WorkItem.WorkflowRunId,
                WorkflowNodeId: w.WorkItem.WorkflowNodeId));
        }

        foreach (var q in queueRuns)
        {
            items.Add(new AgentIntegrationItem(
                q.Integration.Id,
                q.Integration.Name,
                q.Integration.Slug,
                TriggerTypeFor(q.WorkItem),
                null,
                q.Integration.ClassName,
                q.WorkItem.ClaimExpiresAt,
                TriggerSource.Queue,
                null,
                q.Integration.TimeoutSeconds,
                q.WorkItem.Id,
                q.Integration.PackageId,
                q.WorkItem.Payload,
                MessageSubject: q.WorkItem.Message?.Subject ?? q.WorkItem.IntegrationTrigger?.Subject,
                MessageId: q.WorkItem.MessageId,
                MessagePublishedAt: q.WorkItem.Message?.PublishedAt,
                ParentExecutionId: q.WorkItem.ParentExecutionId));
        }

        foreach (var f in fileRuns)
        {
            items.Add(new AgentIntegrationItem(
                f.Integration.Id,
                f.Integration.Name,
                f.Integration.Slug,
                TriggerTypeFor(f.WorkItem),
                null,
                f.Integration.ClassName,
                f.WorkItem.ClaimExpiresAt,
                TriggerSource.File,
                null,
                f.Integration.TimeoutSeconds,
                f.WorkItem.Id,
                f.Integration.PackageId,
                f.WorkItem.Payload));
        }

        return new PollIntegrationsResult(items);
    }

    private static TriggerType TriggerTypeFor(WorkItem workItem) =>
        workItem.IntegrationTrigger?.Type
        ?? (workItem.TriggerSource == TriggerSource.Webhook ? TriggerType.Webhook : TriggerType.Manual);
}
