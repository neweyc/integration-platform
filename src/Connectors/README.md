# Serto.Connectors

Built-in connectors for [Serto](https://github.com/neweyc/integration-platform) integrations. Provides fluent, secret-aware HTTP and SQL access with automatic logging, retries, and pagination.

## Install

```
dotnet add package Serto.Connectors
```

Requires [`Serto.Sdk`](https://www.nuget.org/packages/Serto.Sdk) (pulled in automatically as a dependency).

## HTTP Connector

```csharp
using Serto.Connectors.Http;

var client = context.HttpConnector("https://api.example.com")
    .WithBearerToken("MY_API_KEY")          // secret key, not the value
    .WithRetryPolicy(maxRetries: 3)
    .WithIdempotencyKey(context.Execution.ExecutionId.ToString());

var data   = await client.GetJsonAsync<MyData>("/items", ct);
var result = await client.PostJsonAsync<CreateRequest, CreateResponse>("/items", payload, ct);
```

Authentication options: `WithBearerToken`, `WithApiKeyHeader`, `WithApiKeyQuery`, `WithBasicAuth`.

Pagination helpers:

```csharp
// Cursor / next-link style
var all = await client.GetAllPagesAsync<PageResponse, Item>(
    "/items",
    page => page.Items,
    page => page.NextLink,
    ct);

// Offset / limit style
var all = await client.GetOffsetPagesAsync<PageResponse, Item>(
    "/items",
    page => page.Items,
    page => page.HasMore,
    limit: 100,
    ct: ct);
```

Retries fire automatically on `429` and `5xx`, honoring `Retry-After`. Non-idempotent verbs (`POST`, `PATCH`) are only retried when `WithIdempotencyKey` is set. Query-parameter API keys are redacted from execution logs.

## SQL Connector

Backed by Dapper. Targets SQL Server via the secret-stored connection string.

```csharp
using Serto.Connectors.Sql;

var db = context.SqlConnector("DB_CONN_STRING");

var users = await db.QueryAsync<User>(
    "SELECT * FROM Users WHERE Active = 1", ct: ct);

var rows = await db.ExecuteAsync(
    "INSERT INTO Orders (Id, Total) VALUES (@Id, @Total)",
    new { order.Id, order.Total },
    ct);
```

## Related packages

- [`Serto.Sdk`](https://www.nuget.org/packages/Serto.Sdk) — core interfaces and attributes
- [`Serto.Testing`](https://www.nuget.org/packages/Serto.Testing) — test helpers
