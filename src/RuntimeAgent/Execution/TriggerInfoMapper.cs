using RuntimeAgent.Agent;
using Serto.Sdk;

namespace RuntimeAgent.Execution;

// Maps a poll item's trigger source to the SDK TriggerInfo the integration sees on context.Trigger,
// populating each source's metadata from the poll item.
public static class TriggerInfoMapper
{
    public static TriggerInfo From(IntegrationItem item, DateTime scheduledAt) =>
        item.TriggerSource switch
        {
            "Scheduled" => new ScheduledTrigger(scheduledAt, item.CronExpression),
            "Webhook" => new WebhookTrigger(item.DeliveryId),
            "Manual" => new ManualTrigger(),
            "Workflow" => new WorkflowTrigger(
                item.WorkflowRunId ?? Guid.Empty,
                item.WorkflowNodeId ?? Guid.Empty,
                item.ParentExecutionId),
            "Retry" => new RetryTrigger(item.AttemptNumber),
            "Queue" => new MessageTrigger(
                item.MessageSubject ?? string.Empty,
                item.MessageId ?? Guid.Empty,
                item.MessagePublishedAt ?? scheduledAt,
                item.ParentExecutionId),
            // The File adapter is not active yet; fall back to a neutral manual marker for it and any
            // future source an older agent doesn't recognize.
            _ => new ManualTrigger()
        };
}
