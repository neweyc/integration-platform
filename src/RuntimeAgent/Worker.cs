using Cronos;
using RuntimeAgent.Agent;
using RuntimeAgent.Execution;

namespace RuntimeAgent;

// Polls the control plane on a fixed interval and dispatches integrations that are due.
public class Worker(
    IControlPlaneClient controlPlane,
    IntegrationExecutor executor,
    IntegrationLoader loader,
    AgentOptions options,
    ILogger<Worker> logger) : BackgroundService
{
    // Tracks the last time each integration was dispatched, keyed by integration ID
    private readonly Dictionary<Guid, DateTime> _lastRun = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        loader.LoadFromDirectory(options.IntegrationsPath);

        logger.LogInformation(
            "Agent started — environment: {Environment}, poll interval: {Interval}s",
            options.Environment, options.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await PollAndDispatchAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), stoppingToken);
        }
    }

    private async Task PollAndDispatchAsync(CancellationToken ct)
    {
        try
        {
            var integrations = await controlPlane.GetIntegrationsAsync(ct);

            // Fetch secrets once and share across all executions this cycle
            var secrets = await controlPlane.GetSecretsAsync(ct);

            var now = DateTime.UtcNow;

            foreach (var integration in integrations)
            {
                if (!IsDue(integration, now)) continue;

                _lastRun[integration.Id] = now;

                // Each integration runs concurrently — one slow integration doesn't block others
                _ = executor.ExecuteAsync(integration, secrets, ct)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            logger.LogError(t.Exception, "Unhandled error executing {Name}", integration.Name);
                    }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Poll cycle failed — will retry in {Interval}s", options.PollIntervalSeconds);
        }
    }

    private bool IsDue(IntegrationItem integration, DateTime now)
    {
        if (integration.TriggerType != "Scheduled" || string.IsNullOrEmpty(integration.CronExpression))
            return false;

        try
        {
            var cron = CronExpression.Parse(integration.CronExpression);
            var lastRun = _lastRun.GetValueOrDefault(integration.Id, DateTime.MinValue);
            var next = cron.GetNextOccurrence(lastRun, TimeZoneInfo.Utc);
            return next.HasValue && next.Value <= now;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid cron expression for {Name}: '{Cron}'",
                integration.Name, integration.CronExpression);
            return false;
        }
    }
}
