using IntegrationPlatform.Sdk;
using Microsoft.Extensions.Logging;

namespace TestIntegrations;

/// <summary>
/// A minimal smoke-test integration.
///
/// Register in the control plane with:
///   Name:        Hello World
///   Slug:        hello-world
///   Trigger:     Scheduled
///   Cron:        * * * * *   (every minute, for testing)
///   Environment: production
///
/// Add a secret in the control plane:
///   Environment: production
///   Key:         GREETING
///   Value:       Hello from the agent!
///
/// On each run this integration will:
///   1. Log the execution metadata
///   2. Read the GREETING secret and log it
///   3. Call https://httpbin.org/get and log the response status
/// </summary>
public class HelloWorldIntegration : IIntegration
{
    public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        // 1. Log execution context
        context.Logger.LogInformation(
            "Running {IntegrationName} | ExecutionId: {ExecutionId} | Environment: {Environment} | ScheduledAt: {ScheduledAt}",
            context.Execution.IntegrationName,
            context.Execution.ExecutionId,
            context.Execution.Environment,
            context.Execution.ScheduledAt);

        // 2. Read a secret — demonstrates secrets are injected correctly
        if (context.Secrets.TryGetValue("GREETING", out var greeting))
            context.Logger.LogInformation("Secret GREETING = {Greeting}", greeting);
        else
            context.Logger.LogWarning("Secret GREETING is not configured for this environment.");

        // 3. Make an outbound HTTP call — demonstrates IIntegrationContext.Http works
        context.Logger.LogInformation("Calling httpbin.org...");
        var response = await context.Http.GetAsync("https://httpbin.org/get", ct);
        context.Logger.LogInformation("httpbin.org responded with {StatusCode}", (int)response.StatusCode);

        context.Logger.LogInformation("HelloWorldIntegration completed successfully.");
    }
}
