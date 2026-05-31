using IntegrationPlatform.Sdk;
using RuntimeAgent.Agent;

namespace RuntimeAgent.Execution;

public class IntegrationExecutor(
    IControlPlaneClient controlPlane,
    IntegrationLoader loader,
    IHttpClientFactory httpClientFactory,
    ILogger<IntegrationExecutor> logger)
{
    public async Task ExecuteAsync(IntegrationItem integration, Dictionary<string, string> secrets, CancellationToken ct)
    {
        var instance = loader.Resolve(integration.Slug);
        if (instance is null)
        {
            logger.LogWarning("Skipping {Name} — no matching integration class found for slug '{Slug}'",
                integration.Name, integration.Slug);
            return;
        }

        var executionId = await controlPlane.StartExecutionAsync(integration.Id, ct);
        var integrationLogger = logger; // could create a named logger per integration
        var http = httpClientFactory.CreateClient("integration");
        var metadata = new ExecutionMetadata(
            executionId, integration.Id, integration.Name,
            Environment: "", // set by caller context
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
            await controlPlane.CompleteExecutionAsync(executionId, succeeded: false, errorMessage: ex.Message, ct);
        }
    }
}
