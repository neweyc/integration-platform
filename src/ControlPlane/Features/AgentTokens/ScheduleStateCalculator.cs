using Cronos;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public record ScheduleDecision(
    bool IsDue,
    DateTime? LastDispatchedAt,
    DateTime? NextRunAt);

public static class ScheduleStateCalculator
{
    public static ScheduleDecision Evaluate(
        Integration integration,
        IntegrationScheduleState? state,
        DateTime now)
    {
        if (integration.TriggerType != TriggerType.Scheduled || string.IsNullOrWhiteSpace(integration.CronExpression))
            return new ScheduleDecision(false, state?.LastDispatchedAt, state?.NextRunAt);

        var cron = CronExpression.Parse(integration.CronExpression);
        var nextRunAt = state?.NextRunAt ?? cron.GetNextOccurrence(integration.CreatedAt, TimeZoneInfo.Utc);

        if (nextRunAt is null)
            return new ScheduleDecision(false, state?.LastDispatchedAt, null);

        if (nextRunAt.Value > now)
            return new ScheduleDecision(false, state?.LastDispatchedAt, nextRunAt);

        return new ScheduleDecision(
            true,
            now,
            cron.GetNextOccurrence(now, TimeZoneInfo.Utc));
    }
}
