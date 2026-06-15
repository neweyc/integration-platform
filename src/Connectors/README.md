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

Backed by Dapper, over a secret-stored connection string. Works against **SQL Server, PostgreSQL, MySQL, and Oracle** — the connector handles connection lifecycle, validation, and logging identically; only the underlying ADO.NET provider differs.

```csharp
using Serto.Connectors.Sql;

// Engine-specific helpers (clearest at the call site):
var db = context.PostgresConnector("DB_CONN_STRING");   // or SqlServerConnector / MySqlConnector / OracleConnector

// ...or pass the provider explicitly (defaults to SQL Server):
var db2 = context.SqlConnector("DB_CONN_STRING", SqlProvider.Oracle);

var users = await db.QueryAsync<User>(
    "SELECT * FROM users WHERE active = true", ct: ct);

var rows = await db.ExecuteAsync(
    "INSERT INTO orders (id, total) VALUES (@Id, @Total)",
    new { order.Id, order.Total },
    ct);
```

The connection string is validated against the chosen engine's dialect at construction (so a bad secret fails clearly during `serto test`, not on the first query). Use parameterized SQL — Dapper binds `@Name` parameters for you.

## Related packages

- [`Serto.Sdk`](https://www.nuget.org/packages/Serto.Sdk) — core interfaces and attributes
- [`Serto.Testing`](https://www.nuget.org/packages/Serto.Testing) — test helpers
