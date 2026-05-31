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

    // Tracks which integrations are currently executing to prevent overlapping runs
    private readonly HashSet<Guid> _inFlight = new();
    private readonly object _inFlightLock = new();

    // Limits concurrent executions
    private SemaphoreSlim? _concurrencySemaphore;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        loader.LoadFromDirectory(options.IntegrationsPath);
        _concurrencySemaphore = new SemaphoreSlim(options.MaxConcurrentExecutions, options.MaxConcurrentExecutions);

        logger.LogInformation(
            "Agent started — environment: {Environment}, poll interval: {Interval}s, max concurrent: {MaxConcurrent}",
            options.Environment, options.PollIntervalSeconds, options.MaxConcurrentExecutions);

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

                // Check if this integration is already running
                lock (_inFlightLock)
                {
                    if (_inFlight.Contains(integration.Id))
                    {
                        logger.LogDebug("Skipping {Name} — already executing", integration.Name);
                        continue;
                    }
                }

                _lastRun[integration.Id] = now;

                // Run with concurrency control
                _ = ExecuteWithConcurrencyControlAsync(integration, secrets, ct);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Poll cycle failed — will retry in {Interval}s", options.PollIntervalSeconds);
        }
    }

    private async Task ExecuteWithConcurrencyControlAsync(
        IntegrationItem integration,
        Dictionary<string, string> secrets,
        CancellationToken ct)
    {
        // Mark as in-flight before waiting for semaphore
        lock (_inFlightLock)
        {
            _inFlight.Add(integration.Id);
        }

        try
        {
            // Wait for a concurrency slot
            await _concurrencySemaphore!.WaitAsync(ct);

            try
            {
                await executor.ExecuteAsync(integration, secrets, ct);
            }
            finally
            {
                _concurrencySemaphore.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error executing {Name}", integration.Name);
        }
        finally
        {
            lock (_inFlightLock)
            {
                _inFlight.Remove(integration.Id);
            }
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

    public override void Dispose()
    {
        _concurrencySemaphore?.Dispose();
        base.Dispose();
    }
}
