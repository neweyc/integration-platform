# Writing integrations

An integration is a C# class. You implement one interface and add an attribute that tells Serto how it should run. There's no proprietary DSL and no visual designer — your code is the source of truth.

## The shape

```csharp
[ScheduledIntegration("Order Sync", "order-sync", "*/15 * * * *")]
public class OrderSync : IIntegration
{
    public async Task RunAsync(IIntegrationContext ctx, CancellationToken ct)
    {
        // your logic
    }
}
```

`RunAsync` is plain C#. Do whatever you need — call APIs, query databases, transform data.

## Triggers

The attribute on the class declares when it runs:

- **`[ScheduledIntegration(name, slug, cron)]`** — runs on a cron schedule.
- **`[WebhookIntegration(name, slug)]`** — runs when a signed webhook arrives; the request body is on `ctx.Payload`.
- **`[Integration(name, slug)]`** — no automatic trigger; run it on demand from the dashboard or API.

`serto deploy` provisions the matching trigger automatically. An operator can adjust a schedule in the dashboard later; your code remains the declared default.

## The context

Every run receives an `IIntegrationContext`:

```csharp
public interface IIntegrationContext
{
    IReadOnlyDictionary<string, string> Secrets { get; } // resolved secrets
    ILogger Logger { get; }                              // captured into execution history
    HttpClient Http { get; }                             // outbound calls
    ExecutionMetadata Execution { get; }                 // ids, environment, timing
    string? Payload { get; }                             // webhook body, if any
}
```

## Connectors

Connectors are thin helpers over the context for common targets:

```csharp
var db  = ctx.SqlConnector("ORDERS_DB");
var api = ctx.HttpConnector("https://api.erp.com").WithBearerToken("ERP_API_KEY");

var pending = await db.QueryAsync<Order>(
    "SELECT * FROM Orders WHERE Status = 'Pending'", ct: ct);

foreach (var order in pending)
    await api.PostJsonAsync("/orders", order, ct);
```

## Secrets

You never put credentials in code. Reference a secret by name — `"ERP_API_KEY"` — and configure its value in the control plane. For the strongest isolation, keep the value in an on-prem vault that the agent resolves locally; then the control plane stores only a reference and the credential never touches it.

## Targeting specific agents

If an integration must run on a particular host — wired to hardware, or inside a specific network — declare the capabilities it needs:

```csharp
[ScheduledIntegration("Pulse", "pulse", "* * * * *")]
[RequiresAgentCapabilities("site-floor-1")]
public class Pulse : IIntegration { /* ... */ }
```

The control plane only routes the work to an agent that offers those tags.

## Next steps

- [Architecture](/docs/architecture) — where your code actually runs.
- [Quick start](/docs/quickstart) — deploy your first integration.
