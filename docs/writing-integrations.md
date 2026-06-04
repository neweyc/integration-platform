# Writing integrations

This guide explains how to author, test, and deploy integrations using the **Integration-as-Code** workflow.

---

## The Workflow

The `ip` CLI is the primary tool for development.

1. **Initialize:** `ip init MyProject` scaffolds a new C# project.
2. **Develop:** Write your logic in C#. Use attributes like `[ScheduledIntegration]` to define infrastructure.
3. **Test Locally:** `ip dev` watches for changes and runs your integration instantly.
4. **Deploy:** `ip deploy` builds and auto-provisions your integration in the Control Plane.

---

## Concept

An integration is a C# class that implements `IIntegration`. The class contains your business logic; the platform handles everything else: trigger intake, scheduling, secret injection, execution, logging, and retry.

```csharp
using IntegrationPlatform.Sdk;
using IntegrationPlatform.Connectors.Http;
using IntegrationPlatform.Connectors.Sql;

[ScheduledIntegration("Sync Shopify Orders", "shopify-sync", "0 * * * *")]
public class SyncOrdersIntegration : IIntegration
{
    public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        // Use connectors for common tasks
        var api = context.HttpConnector("https://api.shopify.com")
                         .WithBearerToken("SHOPIFY_API_KEY");

        var db = context.SqlConnector("DATABASE_URL");

        // Fetch from API
        var orders = await api.GetJsonAsync<List<Order>>("/admin/api/orders.json", ct);

        if (orders != null)
        {
            // Write to SQL
            foreach (var order in orders)
            {
                await db.ExecuteAsync(
                    "INSERT INTO Orders (Id, Total) VALUES (@Id, @Total)", 
                    new { order.Id, order.Total }, 
                    ct);
            }

            context.Logger.LogInformation("Synced {Count} orders", orders.Count);
        }
    }
}
```

---

## Local Development & Testing

Use the `ip dev` command for a high-velocity feedback loop.

```bash
ip dev
```

This command:
- Watches your `.cs` files for changes.
- Automatically rebuilds the project.
- Executes the integration in a local test harness.
- Streams logs directly to your terminal.

To test a specific class or provide a mock webhook payload:

```bash
ip test MyIntegration --payload '{"id": 123}'
```

---

## Deployment (Zero-Touch Provisioning)

When you are ready to go live, use the `deploy` command.

```bash
ip deploy --url http://your-control-plane --token pat_...
```

The Control Plane will scan your assembly, discover your classes decorated with integration and trigger attributes, and automatically create or update the executable integration plus its trigger records. This keeps one integration class able to support multiple triggers, such as scheduled and webhook entry points, without duplicating the integration code.

---

## IIntegrationContext

```csharp
public interface IIntegrationContext
{
    // Decrypted secrets for the integration's environment
    IReadOnlyDictionary<string, string> Secrets { get; }

    // Structured logger — output is captured and stored in execution history
    ILogger Logger { get; }

    // Pre-configured HttpClient with sensible defaults
    HttpClient Http { get; }

    // Metadata about the current run
    ExecutionMetadata Execution { get; }

    // Raw request body for Webhook-triggered executions
    string? Payload { get; }
}

public record ExecutionMetadata(
    Guid ExecutionId,
    Guid IntegrationId,
    string IntegrationName,
    string Environment,
    DateTime ScheduledAt);
```

---

## Core Connectors

The platform provides built-in connectors to simplify common integration tasks. To use them, reference the `IntegrationPlatform.Connectors` assembly.

### HTTP Connector

The HTTP connector provides a fluent API for making JSON-based API calls, handling authentication from secrets, and logging requests automatically.

```csharp
var client = context.HttpConnector("https://api.example.com")
                    .WithBearerToken("MY_SECRET_KEY")
                    .WithHeader("X-Custom", "value");

var data = await client.GetJsonAsync<MyData>("/path", ct);
```

### SQL Connector

The SQL connector (built on Dapper) makes it easy to execute queries and commands against SQL Server databases using connection strings stored in secrets.

```csharp
var db = context.SqlConnector("DB_CONN_STRING");
var users = await db.QueryAsync<User>("SELECT * FROM Users WHERE Active = 1", ct: ct);
```

---

## Project structure

A typical integration project:

```
MyIntegration/
  ├── MyIntegration.csproj   # References IntegrationPlatform.Sdk
  ├── MyIntegration.cs       # Your logic with [ScheduledIntegration]
  └── .gitignore
```
