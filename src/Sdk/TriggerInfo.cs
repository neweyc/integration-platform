namespace Serto.Sdk;

/// <summary>
/// What kind of trigger started an execution. Mirrors the platform's trigger sources, exposed to
/// integration code so a run can branch on how it was triggered.
/// </summary>
public enum TriggerKind
{
    Scheduled,
    Webhook,
    Manual,
    Workflow,
    Message,
    Retry
}

/// <summary>
/// Describes how the current run was triggered. Always present on <see cref="IIntegrationContext.Trigger"/>.
/// Pattern-match the concrete type (e.g. <see cref="MessageTrigger"/>) for source-specific details.
/// </summary>
public abstract record TriggerInfo(TriggerKind Kind);

/// <summary>A run started by the integration's cron schedule.</summary>
public sealed record ScheduledTrigger(DateTime ScheduledAt, string? CronExpression)
    : TriggerInfo(TriggerKind.Scheduled);

/// <summary>A run started by an inbound webhook. The body is on <see cref="IIntegrationContext.Payload"/>.</summary>
public sealed record WebhookTrigger(string? DeliveryId)
    : TriggerInfo(TriggerKind.Webhook);

/// <summary>
/// A run started by an operator (or API) on demand. A marker — the platform does not attribute a
/// manual run to a specific user.
/// </summary>
public sealed record ManualTrigger()
    : TriggerInfo(TriggerKind.Manual);

/// <summary>A run started as a node in a workflow.</summary>
public sealed record WorkflowTrigger(Guid WorkflowRunId, Guid WorkflowNodeId, Guid? UpstreamExecutionId)
    : TriggerInfo(TriggerKind.Workflow);

/// <summary>
/// A re-run of a failed execution by the platform's retry policy. <see cref="AttemptNumber"/> is the
/// 1-based attempt this run represents.
/// </summary>
public sealed record RetryTrigger(int AttemptNumber)
    : TriggerInfo(TriggerKind.Retry);

/// <summary>
/// A run started by a published message this integration subscribes to. The message body is on
/// <see cref="IIntegrationContext.Payload"/>; this record carries the sibling metadata (the subject,
/// the message id, when it was published, and the execution that published it — for lineage).
/// </summary>
public sealed record MessageTrigger(string Subject, Guid MessageId, DateTime PublishedAt, Guid? SourceExecutionId)
    : TriggerInfo(TriggerKind.Message);
