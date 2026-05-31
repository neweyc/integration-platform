# Writing integrations

> The runtime agent is not yet built. This document describes the intended developer experience to guide the SDK and agent implementation.

---

## Concept

An integration is a C# class that implements `IIntegration`. The class contains your business logic — the platform handles everything else: scheduling, secret injection, execution, logging, and retry.

```csharp
using IntegrationPlatform.Sdk;

public class SyncOrdersIntegration : IIntegration
{
    public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        var dbUrl = context.Secrets["DATABASE_URL"];
        var apiKey = context.Secrets["SHOPIFY_API_KEY"];

        // your logic here
        var orders = await FetchOrdersFromShopify(apiKey, ct);
        await WriteOrdersToDatabase(dbUrl, orders, ct);

        context.Logger.LogInformation("Synced {Count} orders", orders.Count);
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
    ExecutionContext Execution { get; }
}

public record ExecutionContext(
    Guid IntegrationId,
    string IntegrationName,
    string Environment,
    Guid ExecutionId,
    DateTime ScheduledAt);
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

Once deployed, the integration class must be registered in the control plane UI:

1. Go to **Integrations → New integration**
2. Set the name, slug, environment, and trigger
3. Set the **class name** (fully qualified: `MyIntegrations.SyncOrdersIntegration`)
4. The runtime agent will locate the class in the loaded assembly when dispatching this integration

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

1. Build the integration project: `dotnet publish -c Release`
2. Copy the output `.dll` to the directory the runtime agent is configured to watch
3. The agent hot-reloads the assembly (or restart the agent if hot-reload is not yet implemented)
4. Verify the integration appears as loadable in the agent logs

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
