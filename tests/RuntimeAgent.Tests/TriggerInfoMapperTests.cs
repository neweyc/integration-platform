using RuntimeAgent.Agent;
using RuntimeAgent.Execution;
using Serto.Sdk;

namespace RuntimeAgent.Tests;

public class TriggerInfoMapperTests
{
    private static IntegrationItem Item(string triggerSource) =>
        new(Guid.NewGuid(), "n", "n", triggerSource, null, "Class", null, triggerSource, null);

    [Fact]
    public void Scheduled_CarriesScheduledTimeAndCron()
    {
        var scheduledAt = new DateTime(2026, 6, 10, 9, 0, 0, DateTimeKind.Utc);
        var item = Item("Scheduled") with { CronExpression = "0 * * * *" };

        var trigger = Assert.IsType<ScheduledTrigger>(TriggerInfoMapper.From(item, scheduledAt));
        Assert.Equal(scheduledAt, trigger.ScheduledAt);
        Assert.Equal("0 * * * *", trigger.CronExpression);
    }

    [Fact]
    public void Webhook_CarriesDeliveryId()
    {
        var item = Item("Webhook") with { DeliveryId = "delivery-123" };

        var trigger = Assert.IsType<WebhookTrigger>(TriggerInfoMapper.From(item, DateTime.UtcNow));
        Assert.Equal("delivery-123", trigger.DeliveryId);
    }

    [Fact]
    public void Manual_IsAMarker()
    {
        Assert.IsType<ManualTrigger>(TriggerInfoMapper.From(Item("Manual"), DateTime.UtcNow));
    }

    [Fact]
    public void Workflow_CarriesRunNodeAndUpstreamExecution()
    {
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var upstream = Guid.NewGuid();
        var item = Item("Workflow") with
        {
            WorkflowRunId = runId,
            WorkflowNodeId = nodeId,
            ParentExecutionId = upstream
        };

        var trigger = Assert.IsType<WorkflowTrigger>(TriggerInfoMapper.From(item, DateTime.UtcNow));
        Assert.Equal(runId, trigger.WorkflowRunId);
        Assert.Equal(nodeId, trigger.WorkflowNodeId);
        Assert.Equal(upstream, trigger.UpstreamExecutionId);
    }

    [Fact]
    public void Retry_CarriesAttemptNumber()
    {
        var item = Item("Retry") with { AttemptNumber = 3 };

        var trigger = Assert.IsType<RetryTrigger>(TriggerInfoMapper.From(item, DateTime.UtcNow));
        Assert.Equal(3, trigger.AttemptNumber);
    }

    [Fact]
    public void Queue_CarriesMessageMetadataAndSourceExecution()
    {
        var messageId = Guid.NewGuid();
        var source = Guid.NewGuid();
        var publishedAt = new DateTime(2026, 6, 10, 14, 3, 0, DateTimeKind.Utc);
        var item = Item("Queue") with
        {
            MessageSubject = "high-wind",
            MessageId = messageId,
            MessagePublishedAt = publishedAt,
            ParentExecutionId = source
        };

        var trigger = Assert.IsType<MessageTrigger>(TriggerInfoMapper.From(item, DateTime.UtcNow));
        Assert.Equal("high-wind", trigger.Subject);
        Assert.Equal(messageId, trigger.MessageId);
        Assert.Equal(publishedAt, trigger.PublishedAt);
        Assert.Equal(source, trigger.SourceExecutionId);
    }

    [Fact]
    public void UnknownSource_FallsBackToManualMarker()
    {
        Assert.IsType<ManualTrigger>(TriggerInfoMapper.From(Item("File"), DateTime.UtcNow));
    }
}
