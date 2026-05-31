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
        var instance = loader.Resolve(integration.ClassName);
        if (instance is null)
        {
            logger.LogWarning("Skipping {Name} — no matching integration class found for '{ClassName}'",
                integration.Name, integration.ClassName);
            return;
        }

        var executionId = await controlPlane.StartExecutionAsync(integration.Id, ct);
        var integrationLogger = new ControlPlaneExecutionLogger(logger, controlPlane, executionId, ct);
        var http = httpClientFactory.CreateClient("integration");
        var metadata = new ExecutionMetadata(
            executionId, integration.Id, integration.Name,
            Environment: options.Environment,
            ScheduledAt: DateTime.UtcNow);

        var context = new ExecutionContext(secrets, integrationLogger, http, metadata);

        logger.LogInformation("Starting execution {ExecutionId} for integration {Name}", executionId, integration.Name);

        try
        {
            await instance.RunAsync(context, ct);

            await controlPlane.CompleteExecutionAsync(executionId, succeeded: true, errorMessage: null, ct);
            logger.LogInformation("Execution {ExecutionId} succeeded", executionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Execution {ExecutionId} failed", executionId);
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
