using ControlPlane.Features.AgentTokens;
using Shared.Domain;

namespace ControlPlane.Tests.Features.AgentTokens;

public class ScheduleStateCalculatorTests
{
    [Fact]
    public void Evaluate_NewTriggerNotYetDue_InitializesNextRun()
    {
        var createdAt = new DateTime(2026, 6, 1, 10, 1, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 6, 1, 10, 30, 0, DateTimeKind.Utc);
        var trigger = ScheduledTrigger(createdAt, "0 * * * *");

        var decision = ScheduleStateCalculator.Evaluate(trigger, state: null, now);

        Assert.False(decision.IsDue);
        Assert.Null(decision.LastDispatchedAt);
        Assert.Equal(new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc), decision.NextRunAt);
    }

    [Fact]
    public void Evaluate_NewTriggerDue_DispatchesAndAdvancesNextRun()
    {
        var createdAt = new DateTime(2026, 6, 1, 10, 1, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc);
        var trigger = ScheduledTrigger(createdAt, "0 * * * *");

        var decision = ScheduleStateCalculator.Evaluate(trigger, state: null, now);

        Assert.True(decision.IsDue);
        Assert.Equal(now, decision.LastDispatchedAt);
        Assert.Equal(new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc), decision.NextRunAt);
    }

    [Fact]
    public void Evaluate_ExistingFutureNextRun_DoesNotDispatch()
    {
        var now = new DateTime(2026, 6, 1, 10, 30, 0, DateTimeKind.Utc);
        var trigger = ScheduledTrigger(now.AddHours(-1), "0 * * * *");
        var state = new IntegrationScheduleState
        {
            TenantId = Guid.NewGuid(),
            IntegrationId = Guid.NewGuid(),
            IntegrationTriggerId = trigger.Id,
            LastDispatchedAt = now.AddMinutes(-30),
            NextRunAt = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc)
        };

        var decision = ScheduleStateCalculator.Evaluate(trigger, state, now);

        Assert.False(decision.IsDue);
        Assert.Equal(state.LastDispatchedAt, decision.LastDispatchedAt);
        Assert.Equal(state.NextRunAt, decision.NextRunAt);
    }

    [Fact]
    public void Evaluate_ExistingDueNextRun_DispatchesAndAdvancesNextRun()
    {
        var now = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc);
        var trigger = ScheduledTrigger(now.AddHours(-2), "0 * * * *");
        var state = new IntegrationScheduleState
        {
            TenantId = Guid.NewGuid(),
            IntegrationId = Guid.NewGuid(),
            IntegrationTriggerId = trigger.Id,
            LastDispatchedAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            NextRunAt = now
        };

        var decision = ScheduleStateCalculator.Evaluate(trigger, state, now);

        Assert.True(decision.IsDue);
        Assert.Equal(now, decision.LastDispatchedAt);
        Assert.Equal(new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc), decision.NextRunAt);
    }

    [Fact]
    public void Evaluate_DisabledTrigger_DoesNotDispatch()
    {
        var trigger = ScheduledTrigger(DateTime.UtcNow.AddDays(-1), "0 * * * *");
        trigger.Enabled = false;

        var decision = ScheduleStateCalculator.Evaluate(trigger, state: null, DateTime.UtcNow);

        Assert.False(decision.IsDue);
        Assert.Null(decision.NextRunAt);
    }

    private static IntegrationTrigger ScheduledTrigger(DateTime createdAt, string cronExpression) => new()
    {
        CreatedAt = createdAt,
        Type = TriggerType.Scheduled,
        Enabled = true,
        CronExpression = cronExpression
    };
}
