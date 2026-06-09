namespace ControlPlane.Features.Alerts;

// Drains the alert queue and delivers each alert off the request path. Runs one alert at a time in its
// own DI scope (the notifier and its repository are scoped), mirroring the OrphanedExecutionReaper.
public class AlertDispatchService(
    AlertDispatchQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<AlertDispatchService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Alert dispatch service started");

        await foreach (var alert in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await DispatchAsync(alert, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // A single bad alert must never take the dispatcher down — log and move on.
                logger.LogError(ex, "Failed to dispatch alert for execution {ExecutionId}", alert.ExecutionId);
            }
        }
    }

    private async Task DispatchAsync(FailedExecutionAlert alert, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IAlertNotifier>();

        var outcome = await notifier.SendAsync(alert, ct);

        if (!outcome.AnyAttempted)
            return; // No channel configured for this integration — nothing to do.

        logger.LogInformation(
            "Dispatched failure alert for integration {Integration} ({Environment}) — email: {Email}, webhook: {Webhook}",
            alert.IntegrationName,
            alert.Environment,
            DescribeChannel(outcome.EmailAttempted, outcome.EmailSucceeded),
            DescribeChannel(outcome.WebhookAttempted, outcome.WebhookSucceeded));
    }

    private static string DescribeChannel(bool attempted, bool succeeded) =>
        !attempted ? "skipped" : succeeded ? "sent" : "failed";
}
