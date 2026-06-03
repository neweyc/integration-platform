# Writing integrations

This guide explains how to write, deploy, and test integrations for the platform.

---

## Concept

An integration is a C# class that implements `IIntegration`. The class contains your business logic; the platform handles everything else: trigger intake, scheduling, secret injection, execution, logging, and retry.

```csharp
using IntegrationPlatform.Sdk;
using IntegrationPlatform.Connectors.Http;
using IntegrationPlatform.Connectors.Sql;

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
MyIntegrations/
  MyIntegrations.csproj
  SyncOrders/
    SyncOrdersIntegration.cs
  SyncInventory/
    SyncInventoryIntegration.cs
  README.md
```

The project targets `net10.0` and references the SDK package:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="IntegrationPlatform.Sdk" Version="1.0.0" />
  </ItemGroup>
</Project>
```

---

## Registration

Once deployed, the integration class must be registered in the control plane:

1. Go to **Integrations → New integration**
2. Fill in the required fields:
   - **Name** — display name (e.g. "Sync Orders")
   - **Slug** — URL-safe identifier (e.g. `sync-orders`)
   - **Environment** — target environment (e.g. `production`)
   - **Trigger type** — built-in values are `Scheduled`, `Webhook`, or `Manual`
   - **Cron expression** — required for scheduled triggers (e.g. `0 * * * *` for hourly)
   - **Class name** — fully qualified .NET type name (e.g. `MyIntegrations.SyncOrdersIntegration`)
3. The runtime agent uses the **class name** to locate and instantiate the integration class when work is claimed

Triggers are producers. Whether an integration is started by cron, a manual run, a webhook, or a future queue/file/database trigger, the platform converts that event into a work item and the agent executes the same integration class through the same runtime path. Webhook payloads are available through `context.Payload`; future adapters should expose normalized payload or metadata the same way.

> **Important:** The class name must exactly match the fully qualified type name in your assembly. If the agent can't find the class, it will log a warning and skip execution.

---

## Secrets

Secrets are injected at execution time — your code never deals with decryption or API calls to fetch them. Access them via `context.Secrets["KEY"]`.

If a required secret is missing, throw a descriptive exception:

```csharp
if (!context.Secrets.TryGetValue("SHOPIFY_API_KEY", out var apiKey))
    throw new InvalidOperationException("SHOPIFY_API_KEY is not configured for this environment.");
```

The execution will be marked as failed and the error logged.

---

## Error handling

Unhandled exceptions are caught by the agent, the execution is marked as failed, and the exception details are logged. You do not need to wrap your entire integration in try/catch unless you want to handle specific errors and continue.

For expected failures (e.g. a downstream system is temporarily unavailable), throw a descriptive exception with a clear message. The agent will retry according to the integration's retry configuration (coming in Phase 2).

---

## Logging

Use `context.Logger` — it writes to the execution log stored in the control plane, visible in the UI:

```csharp
context.Logger.LogInformation("Processing batch of {Count} records", records.Count);
context.Logger.LogWarning("Rate limit hit, backing off for 5 seconds");
context.Logger.LogError(ex, "Failed to connect to ERP");
```

Avoid `Console.WriteLine` — output is not captured.

---

## Deploying integrations

The control plane stores versioned integration package zip files, and runtime agents can sync those packages into their configured `PackagesPath`. Agents also still load DLLs from their local `IntegrationsPath`, which is useful for local development.

1. Build the integration project: `dotnet publish -c Release`
2. Create a package archive from the publish output:
   ```bash
   cd bin/Release/net10.0/publish
   zip -r integrations.zip .
   ```
3. Upload the archive to the control plane:
   ```bash
   curl -X POST http://localhost:5000/api/integration-packages \
     -H "Authorization: Bearer <jwt>" \
     -F "name=MyCompany.Integrations" \
     -F "version=1.0.0" \
     -F "file=@integrations.zip"
   ```
4. Wait for the runtime agent to sync packages, or restart it to sync immediately
5. Verify the integration appears in the agent logs: `Loaded integration: MyCompany.Integrations.SyncOrdersIntegration`

Package constraints:

- The uploaded file must be a valid `.zip`
- The archive must contain at least one `.dll`
- The archive must be 100 MB or smaller
- The package `(name, version)` pair must be unique within the tenant

Current limitations:

- Packages are tenant-scoped, not environment-scoped
- Integrations can be pinned to a package/version, and execution records retain the package version that ran
- Rollback is performed by repointing an integration to a previous package version rather than through a dedicated rollback workflow
- Loaded assemblies are not isolated or unloaded yet

---

## Testing integrations

Because integrations are plain C# classes, they are easy to unit test. Mock `IIntegrationContext` with NSubstitute:

```csharp
public class SyncOrdersIntegrationTests
{
    [Fact]
    public async Task RunAsync_LogsOrderCount()
    {
        var context = Substitute.For<IIntegrationContext>();
        context.Secrets.Returns(new Dictionary<string, string>
        {
            ["SHOPIFY_API_KEY"] = "test-key",
            ["DATABASE_URL"] = "postgres://localhost/test",
        });
        context.Logger.Returns(NullLogger.Instance);
        context.Http.Returns(new HttpClient(new MockHttpHandler()));

        var integration = new SyncOrdersIntegration();
        await integration.RunAsync(context, CancellationToken.None);

        context.Logger.Received().LogInformation(
            "Synced {Count} orders", Arg.Any<int>());
    }
}
```
