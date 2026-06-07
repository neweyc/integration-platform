# Serto.Testing

Test helpers for [Serto](https://github.com/neweyc/integration-platform) integrations. Provides an in-memory `IIntegrationContext` and a one-liner integration runner for unit tests.

## Install

```
dotnet add package Serto.Testing
```

## IntegrationTester

Run an integration in a single line:

```csharp
using Serto.Testing;

await IntegrationTester.RunAsync<SyncOrdersIntegration>(
    secrets: new() { ["SHOPIFY_API_KEY"] = "test-token" },
    payload: null);
```

## TestIntegrationContext

Construct and configure the context directly for more control:

```csharp
using Serto.Testing;
using Microsoft.Extensions.Logging;

var context = new TestIntegrationContext
{
    Secrets = new Dictionary<string, string> { ["API_KEY"] = "test" },
    Logger  = loggerFactory.CreateLogger("test"),
    Http    = new HttpClient(mockHandler),
    Payload = """{"id": 42}"""
};

var integration = new MyIntegration();
await integration.RunAsync(context, CancellationToken.None);
```

## Related packages

- [`Serto.Sdk`](https://www.nuget.org/packages/Serto.Sdk) — core interfaces and attributes
- [`Serto.Connectors`](https://www.nuget.org/packages/Serto.Connectors) — HTTP and SQL connectors
