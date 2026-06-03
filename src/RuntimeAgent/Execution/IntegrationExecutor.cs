using IntegrationPlatform.Sdk;
using RuntimeAgent.Agent;

namespace RuntimeAgent.Execution;

public class IntegrationExecutor(
    IControlPlaneClient controlPlane,
    IntegrationLoader loader,
    IHttpClientFactory httpClientFactory,
    AgentOptions options,
    ILogger<IntegrationExecutor> logger)
{
    public async Task ExecuteAsync(IntegrationItem integration, Dictionary<string, string> secrets, CancellationToken ct)
    {
        IIntegration? instance;
        if (integration.PackageId.HasValue)
        {
            var packageDir = Path.Combine(options.PackagesPath, integration.PackageId.Value.ToString());
            instance = loader.ResolveFromDirectory(integration.ClassName, packageDir);
        }
        else
        {
            instance = loader.Resolve(integration.ClassName);
        }

        if (instance is null)
        {
            logger.LogWarning("Skipping {Name}: no matching integration class found for '{ClassName}'",
                integration.Name, integration.ClassName);
            return;
        }

        if (!integration.WorkItemId.HasValue)
        {
            logger.LogWarning("Skipping {Name}: no work item ID provided", integration.Name);
            return;
        }

        var executionId = await controlPlane.StartExecutionAsync(integration.WorkItemId.Value, ct);
        var integrationLogger = new ControlPlaneExecutionLogger(logger, controlPlane, executionId);
        var http = httpClientFactory.CreateClient("integration");
        var metadata = new ExecutionMetadata(
            executionId, integration.Id, integration.Name,
            Environment: options.Environment,
            ScheduledAt: DateTime.UtcNow);

        var context = new ExecutionContext(secrets, integrationLogger, http, metadata, integration.Payload);

        logger.LogInformation("Starting execution {ExecutionId} for integration {Name}", executionId, integration.Name);

        // Create a per-execution CTS that also enforces the configured timeout.
        // Linked to ct so that agent shutdown still cancels this CTS.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (integration.TimeoutSeconds.HasValue)
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(integration.TimeoutSeconds.Value));

        try
        {
            await instance.RunAsync(context, timeoutCts.Token);

            await integrationLogger.FlushAsync(ct);
            await controlPlane.CompleteExecutionAsync(executionId, succeeded: true, errorMessage: null, ct);
            logger.LogInformation("Execution {ExecutionId} succeeded", executionId);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested && integration.TimeoutSeconds.HasValue)
        {
            // Timeout fired before agent shutdown; report as a distinct timed-out status.
            logger.LogWarning("Execution {ExecutionId} timed out after {Timeout}s",
                executionId, integration.TimeoutSeconds.Value);
            using var reportCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await integrationLogger.FlushAsync(reportCts.Token);
            await controlPlane.CompleteExecutionAsync(
                executionId, succeeded: false,
                errorMessage: $"Execution timed out after {integration.TimeoutSeconds.Value}s",
                reportCts.Token,
                isTimeout: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Agent is shutting down; use a fresh token so the report can still reach the control plane.
            logger.LogWarning("Execution {ExecutionId} cancelled: agent is shutting down", executionId);
            using var reportCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await integrationLogger.FlushAsync(reportCts.Token);
            await controlPlane.CompleteExecutionAsync(
                executionId, succeeded: false,
                errorMessage: "Execution cancelled: agent shutting down",
                reportCts.Token);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Execution {ExecutionId} failed", executionId);
            await integrationLogger.FlushAsync(ct);
            await controlPlane.RecordLogAsync(
                executionId,
                new ExecutionLogEntry(
                    DateTime.UtcNow,
                    LogLevel.Error.ToString(),
                    $"Execution failed: {ex.Message}",
                    ex.ToString(),
                    null),
                ct);
            await controlPlane.CompleteExecutionAsync(executionId, succeeded: false, errorMessage: ex.Message, ct);
        }
    }
}
