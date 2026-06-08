using ControlPlane.Infrastructure;

namespace ControlPlane.Features.AgentTokens;

public class OrphanedExecutionReaperOptions
{
    // How often the reaper sweeps for stuck executions.
    public int SweepIntervalSeconds { get; set; } = 60;

    // Grace added to an integration's configured timeout before its running execution is considered
    // orphaned. Covers the agent's own shutdown/report window so a healthy late finish is not reaped.
    public int TimeoutGraceSeconds { get; set; } = 120;

    // Ceiling for integrations that declare no timeout. Generous by default so genuinely long-running
    // integrations are not killed; lower it if your workloads are known to be short.
    public int DefaultMaxRunningSeconds { get; set; } = 3600;
}

// Periodically closes out executions stuck in Running because the agent never reported a terminal
// result. Invokes the reaper handler directly inside a scope rather than via the auditing dispatcher,
// since this is a system action with no acting user.
public class OrphanedExecutionReaper(
    IServiceScopeFactory scopeFactory,
    OrphanedExecutionReaperOptions options,
    ILogger<OrphanedExecutionReaper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Orphaned execution reaper started — sweep interval: {Interval}s, default ceiling: {Ceiling}s",
            options.SweepIntervalSeconds, options.DefaultMaxRunningSeconds);

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
                // A failed sweep must not take the background service down — log and retry next cycle.
                logger.LogError(ex, "Orphaned execution sweep failed — will retry in {Interval}s",
                    options.SweepIntervalSeconds);
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<ReapOrphanedExecutionsCommand, ReapOrphanedExecutionsResult>>();

        var result = await handler.HandleAsync(new ReapOrphanedExecutionsCommand(DateTime.UtcNow), ct);

        if (result.ReapedCount > 0)
            logger.LogWarning("Reaped {Count} orphaned execution(s) stuck in Running", result.ReapedCount);
    }
}
