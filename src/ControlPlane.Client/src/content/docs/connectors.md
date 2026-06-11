# Connectors

Connectors are small helpers over the integration context for the things integrations do constantly — calling HTTP APIs and querying databases. They handle auth, retries, and serialization so your `RunAsync` stays focused on logic. They're optional: `ctx.Http` (a plain `HttpClient`) is always available if you'd rather.

## HTTP connector

Create one from the context with a base URL, then chain auth and options:

```csharp
var api = ctx.HttpConnector("https://api.erp.com")
             .WithBearerToken("ERP_API_KEY")
             .WithRetryPolicy(maxAttempts: 3);
```

Auth and configuration — each `With…` returns the connector, so they chain:

- `WithBearerToken(secretKey)` — `Authorization: Bearer <secret>`
- `WithApiKeyHeader(headerName, secretKey)` — API key in a header
- `WithApiKeyQuery(parameterName, secretKey)` — API key in the query string
- `WithBasicAuth(usernameSecretKey, passwordSecretKey)` — HTTP basic auth
- `WithHeader(name, value)` — a fixed header
- `WithQueryParameter(name, value)` — a fixed query parameter
- `WithIdempotencyKey(key)` — send an idempotency-key header
- `WithRetryPolicy(...)` — retry transient failures

Secret arguments are **secret names**, never values — the value is resolved at run time (see [Secrets](/docs/secrets)).

Requests:

```csharp
var order   = await api.GetJsonAsync<Order>("/orders/42", ct);
await api.PostJsonAsync("/orders", newOrder, ct);
var created = await api.PostJsonAsync<NewOrder, Order>("/orders", newOrder, ct);
await api.DeleteAsync("/orders/42", ct);
```

For large collections the connector can walk pages for you with `GetAllPagesAsync` / `GetOffsetPagesAsync`. For full control, `SendAsync` returns the raw `HttpResponseMessage`.

## SQL connector

Create one from a connection-string secret, then query or execute:

```csharp
var db = ctx.SqlConnector("ORDERS_DB"); // "ORDERS_DB" is the secret holding the connection string

var pending = await db.QueryAsync<Order>(
    "SELECT * FROM Orders WHERE Status = @status",
    new { status = "Pending" }, ct);

await db.ExecuteAsync(
    "UPDATE Orders SET Status = 'Synced' WHERE Id = @id",
    new { id = order.Id }, ct);
```

`QueryAsync<T>` maps rows to `T`; `ExecuteAsync` returns the affected row count. Parameters are passed as an anonymous object and bound safely — no string concatenation.

## Beyond the built-ins

Connectors are just extension methods on `IIntegrationContext` plus a class — nothing about them is privileged. Write your own for an internal system, ship it as a NuGet package, and your team uses it exactly like the built-in ones.

## Next steps

- [Secrets](/docs/secrets) — how credentials reach a connector without living in code.
- [Writing integrations](/docs/writing-integrations)
