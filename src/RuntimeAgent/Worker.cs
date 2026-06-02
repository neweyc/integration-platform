using RuntimeAgent.Agent;
using RuntimeAgent.Execution;

namespace RuntimeAgent;

// Polls the control plane on a fixed interval and dispatches integrations that are due.
public class Worker(
    IControlPlaneClient controlPlane,
    IntegrationExecutor executor,
    IntegrationLoader loader,
    PackageSyncer syncer,
    AgentOptions options,
    ILogger<Worker> logger) : BackgroundService
{
    // Tracks in-flight executions: integration ID → (task, per-execution CTS)
    // The CTS is independent of stoppingToken so the drain window can control cancellation.
    private readonly Dictionary<Guid, (Task Task, CancellationTokenSource Cts)> _inFlight = new();
    private readonly object _inFlightLock = new();
    private SemaphoreSlim? _concurrencySemaphore;
    private DateTime _lastPackageSync = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        loader.LoadFromDirectory(options.IntegrationsPath);
        _concurrencySemaphore = new SemaphoreSlim(options.MaxConcurrentExecutions, options.MaxConcurrentExecutions);

        logger.LogInformation(
            "Agent started — environment: {Environment}, poll interval: {Interval}s, max concurrent: {MaxConcurrent}",
            options.Environment, options.PollIntervalSeconds, options.MaxConcurrentExecutions);

        // Sync packages before the first poll so integrations are available immediately
        await SyncPackagesAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (DateTime.UtcNow - _lastPackageSync >= TimeSpan.FromSeconds(options.PackageSyncIntervalSeconds))
                await SyncPackagesAsync(stoppingToken);

            await PollAndDispatchAsync(stoppingToken);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop the polling loop
        await base.StopAsync(cancellationToken);

        (Task Task, CancellationTokenSource Cts)[] inFlight;
        lock (_inFlightLock)
        {
            inFlight = [.. _inFlight.Values];
        }

        if (inFlight.Length == 0)
            return;

        logger.LogInformation(
            "Draining {Count} in-flight execution(s) — drain window: {Drain}s",
            inFlight.Length, options.ShutdownDrainSeconds);

        var allTasks = inFlight.Select(x => x.Task).ToArray();

        try
        {
            await Task.WhenAll(allTasks)
                .WaitAsync(TimeSpan.FromSeconds(options.ShutdownDrainSeconds), CancellationToken.None);

            logger.LogInformation("All in-flight executions completed within drain window");
        }
        catch (TimeoutException)
        {
            var remaining = allTasks.Count(t => !t.IsCompleted);
            logger.LogWarning("Drain window expired — cancelling {Count} remaining execution(s)", remaining);

            foreach (var (_, cts) in inFlight)
                cts.Cancel();

            await Task.WhenAll(allTasks);
        }
    }

    private async Task SyncPackagesAsync(CancellationToken stoppingToken)
    {
        await syncer.SyncAsync(stoppingToken);
        _lastPackageSync = DateTime.UtcNow;
    }

    private async Task PollAndDispatchAsync(CancellationToken stoppingToken)
    {
        try
        {
            // The control plane returns only integrations that are due AND have been claimed with a lease.
            var integrations = await controlPlane.GetIntegrationsAsync(stoppingToken);

            if (integrations.Count == 0)
                return;

            // Fetch secrets once and share across all executions this cycle
            var secrets = await controlPlane.GetSecretsAsync(stoppingToken);

            foreach (var integration in integrations)
            {
                lock (_inFlightLock)
                {
                    if (_inFlight.ContainsKey(integration.Id))
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

                StartExecution(integration, secrets);
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Poll cycle failed — will retry in {Interval}s", options.PollIntervalSeconds);
        }
    }

    private void StartExecution(IntegrationItem integration, Dictionary<string, string> secrets)
    {
        var executionCts = new CancellationTokenSource();
        // Task is created and begins executing synchronously until its first true async point.
        // It is registered in _inFlight before PollAndDispatchAsync continues.
        var task = RunExecutionAsync(integration, secrets, executionCts);

        lock (_inFlightLock)
        {
            _inFlight[integration.Id] = (task, executionCts);
        }
    }

    private async Task RunExecutionAsync(
        IntegrationItem integration,
        Dictionary<string, string> secrets,
        CancellationTokenSource executionCts)
    {
        try
        {
            await _concurrencySemaphore!.WaitAsync(executionCts.Token);
            try
            {
                await executor.ExecuteAsync(integration, secrets, executionCts.Token);
            }
            finally
            {
                _concurrencySemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Execution was cancelled after the drain window expired — executor already reported failure
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
            executionCts.Dispose();
        }
    }

    public override void Dispose()
    {
        _concurrencySemaphore?.Dispose();
        base.Dispose();
    }
}
