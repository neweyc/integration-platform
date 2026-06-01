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
            // The control plane returns only integrations that are due AND have been claimed with a lease.
            // Leases prevent duplicate dispatch across multiple agents or poll cycles.
            var integrations = await controlPlane.GetIntegrationsAsync(ct);

            if (integrations.Count == 0)
                return;

            // Fetch secrets once and share across all executions this cycle
            var secrets = await controlPlane.GetSecretsAsync(ct);

            foreach (var integration in integrations)
            {
                // Check if this integration is already running locally
                lock (_inFlightLock)
                {
                    if (_inFlight.Contains(integration.Id))
                    {
                        logger.LogDebug("Skipping {Name} — already executing locally", integration.Name);
                        continue;
                    }
                }

                if (integration.LeaseExpiresAt.HasValue)
                {
                    logger.LogDebug(
                        "Dispatching {Name} (lease expires at {LeaseExpiresAt})",
                        integration.Name,
                        integration.LeaseExpiresAt.Value);
                }

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

    public override void Dispose()
    {
        _concurrencySemaphore?.Dispose();
        base.Dispose();
    }
}
