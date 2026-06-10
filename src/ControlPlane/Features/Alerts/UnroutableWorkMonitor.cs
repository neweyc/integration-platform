using ControlPlane.Infrastructure;

namespace ControlPlane.Features.Alerts;

public class UnroutableWorkMonitorOptions
{
    // Lets operators turn the monitor off without removing alert configuration.
    public bool Enabled { get; set; } = true;

    // How often to re-check routability. Aligned with the ~2-minute agent staleness window.
    public int SweepIntervalSeconds { get; set; } = 120;
}

// Periodically detects integrations no live agent can route to and sends an alert through the same
// channels as failure alerts. Deduped by the handler (alert once per transition into unroutable),
// modeled on OrphanedExecutionReaper.
public class UnroutableWorkMonitor(
    IServiceScopeFactory scopeFactory,
    UnroutableWorkMonitorOptions options,
    ILogger<UnroutableWorkMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Unroutable work monitor is disabled");
            return;
        }

        logger.LogInformation(
            "Unroutable work monitor started — sweep interval: {Interval}s", options.SweepIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.SweepIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Unroutable work sweep failed — will retry in {Interval}s", options.SweepIntervalSeconds);
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<MonitorUnroutableWorkCommand, MonitorUnroutableWorkResult>>();

        var result = await handler.HandleAsync(new MonitorUnroutableWorkCommand(DateTime.UtcNow), ct);

        if (result.NewlyUnroutable.Count == 0)
            return;

        // Dedup state was already persisted by the handler, so a delivery failure won't re-alert next
        // sweep — the always-visible UI banner is the backstop. Send is best-effort, like failure alerts.
        var notifier = scope.ServiceProvider.GetRequiredService<IAlertNotifier>();
        foreach (var alert in result.NewlyUnroutable)
        {
            try
            {
                var outcome = await notifier.SendAsync(alert, ct);
                logger.LogWarning(
                    "Integration {Slug} ({Environment}) is unroutable — needs [{Tags}]; alert delivery attempted: {Attempted}",
                    alert.Slug, alert.Environment, string.Join(", ", alert.RequiredTags), outcome.AnyAttempted);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to send unroutable alert for {Slug}", alert.Slug);
            }
        }
    }
}
