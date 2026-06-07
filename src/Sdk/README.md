# Serto.Sdk

The core SDK for building code-first integrations on [Serto](https://github.com/neweyc/integration-platform) — the developer integration platform that replaces click-ops IPaaS tools with C# code you own.

## Install

```
dotnet add package Serto.Sdk
```

## Usage

Implement `IIntegration` and decorate with a trigger attribute. The platform handles scheduling, secret injection, execution, logging, and retry.

```csharp
using Serto.Sdk;

[ScheduledIntegration(
    "Sync Orders",
    "sync-orders",
    "0 * * * *",
    TimeoutSeconds = 300,
    RetryMaxAttempts = 2)]
public class SyncOrdersIntegration : IIntegration
{
    public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        var token = context.Secrets["SHOPIFY_API_KEY"];
        context.Logger.LogInformation("Starting sync for run {Id}", context.Execution.ExecutionId);

        // your logic here
    }
}
```

## IIntegrationContext

| Member | Description |
|---|---|
| `Secrets` | Decrypted secrets for the integration's environment |
| `Logger` | Structured logger — output is stored in execution history |
| `Http` | Pre-configured `HttpClient` |
| `Execution` | Metadata: execution ID, integration ID, environment, scheduled time |
| `Payload` | Raw request body for webhook-triggered executions |

## Trigger attributes

| Attribute | Use |
|---|---|
| `[ScheduledIntegration]` | Cron-scheduled trigger |
| `[WebhookIntegration]` | HTTP webhook trigger |
| `[Integration]` | Executable metadata only, no stored trigger |

## Related packages

- [`Serto.Connectors`](https://www.nuget.org/packages/Serto.Connectors) — HTTP and SQL connectors
- [`Serto.Testing`](https://www.nuget.org/packages/Serto.Testing) — test helpers
