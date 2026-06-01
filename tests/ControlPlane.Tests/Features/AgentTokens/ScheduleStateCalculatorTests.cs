using ControlPlane.Features.AgentTokens;
using Shared.Domain;

namespace ControlPlane.Tests.Features.AgentTokens;

public class ScheduleStateCalculatorTests
{
    [Fact]
    public void Evaluate_NewIntegrationNotYetDue_InitializesNextRun()
    {
        var createdAt = new DateTime(2026, 6, 1, 10, 1, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 6, 1, 10, 30, 0, DateTimeKind.Utc);
        var integration = ScheduledIntegration(createdAt, "0 * * * *");

        var decision = ScheduleStateCalculator.Evaluate(integration, state: null, now);

        Assert.False(decision.IsDue);
        Assert.Null(decision.LastDispatchedAt);
        Assert.Equal(new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc), decision.NextRunAt);
    }

    [Fact]
    public void Evaluate_NewIntegrationDue_DispatchesAndAdvancesNextRun()
    {
        var createdAt = new DateTime(2026, 6, 1, 10, 1, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc);
        var integration = ScheduledIntegration(createdAt, "0 * * * *");

        var decision = ScheduleStateCalculator.Evaluate(integration, state: null, now);

        Assert.True(decision.IsDue);
        Assert.Equal(now, decision.LastDispatchedAt);
        Assert.Equal(new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc), decision.NextRunAt);
    }

    [Fact]
    public void Evaluate_ExistingFutureNextRun_DoesNotDispatch()
    {
        var now = new DateTime(2026, 6, 1, 10, 30, 0, DateTimeKind.Utc);
        var integration = ScheduledIntegration(now.AddHours(-1), "0 * * * *");
        var state = new IntegrationScheduleState
        {
            LastDispatchedAt = now.AddMinutes(-30),
            NextRunAt = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc)
        };

        var decision = ScheduleStateCalculator.Evaluate(integration, state, now);

        Assert.False(decision.IsDue);
        Assert.Equal(state.LastDispatchedAt, decision.LastDispatchedAt);
        Assert.Equal(state.NextRunAt, decision.NextRunAt);
    }

    [Fact]
    public void Evaluate_ExistingDueNextRun_DispatchesAndAdvancesNextRun()
    {
        var now = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc);
        var integration = ScheduledIntegration(now.AddHours(-2), "0 * * * *");
        var state = new IntegrationScheduleState
        {
            LastDispatchedAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            NextRunAt = now
        };

        var decision = ScheduleStateCalculator.Evaluate(integration, state, now);

        Assert.True(decision.IsDue);
        Assert.Equal(now, decision.LastDispatchedAt);
        Assert.Equal(new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc), decision.NextRunAt);
    }

    [Fact]
    public void Evaluate_ManualIntegration_DoesNotDispatch()
    {
        var integration = new Integration
        {
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            TriggerType = TriggerType.Manual,
            CronExpression = null
        };

        var decision = ScheduleStateCalculator.Evaluate(integration, state: null, DateTime.UtcNow);

        Assert.False(decision.IsDue);
        Assert.Null(decision.NextRunAt);
    }

    private static Integration ScheduledIntegration(DateTime createdAt, string cronExpression)
    {
        return new Integration
        {
            CreatedAt = createdAt,
            TriggerType = TriggerType.Scheduled,
            CronExpression = cronExpression
        };
    }
}
