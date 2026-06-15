# SDK, Connectors, Trigger Adapters, And Integrations

This platform should stay code-first, but code-first should not mean every team rewrites the same HTTP pagination, SQL batching, SFTP file movement, object storage, retry, and logging boilerplate.

## Philosophy: connect by protocol, not by vendor

Low-code platforms (Boomi, Workato, MuleSoft, n8n) build connectors **per vendor** — a Salesforce connector, a Stripe connector, a NetSuite connector. That set is infinite, so "1,200 connectors" becomes both the moat and the headline metric. Serto does not play that game and does not need to.

In a code-first tool, **the language ecosystem is the connector library.** NuGet already has `Stripe.net`, the AWS and Azure SDKs, `Npgsql`, `Oracle.ManagedDataAccess`; PyPI and npm have the rest. A "Stripe connector" is just the HTTP connector pointed at Stripe's API, or `dotnet add package Stripe.net`. You never wait for us to build an adapter.

So Serto connects **per protocol/transport** — HTTP, SQL, message queue, object store, SFTP. That set is small and bounded (a handful, ever), and every vendor reaches you through one of them.

### Litmus test

A connector ships **only if** it abstracts a cross-cutting concern that is:

1. error-prone,
2. repeated across most integrations, and
3. not already handled by the vendor's own SDK.

- **Passes** (transport-level): secret injection, auth schemes, retry/backoff/idempotency, pagination, connection lifecycle, structured logging, redaction.
- **Fails** (per-vendor): "a Stripe connector," "a Salesforce connector." Use the vendor package directly.

### The obligation this creates

Because the set is small, each connector must be genuinely best-in-class. **A thin connector is worse than none** — it invites the "your connectors are shallow" critique, whereas a missing one just means "use the package you already know." The HTTP connector sets the bar (auth, retries, idempotency, pagination, log redaction); every other connector must meet it. (This is why the SQL connector is multi-provider, not SQL-Server-only — see below.)

### Where the framing is weak — stated honestly

- The batteries (secrets, retry, logging) only fully apply on the connector paths. Code that uses a vendor SDK directly pulls secrets from the context but is otherwise on its own rails.
- Buyers conditioned by low-code tools will ask "do you have a connector for X?" For a developer audience, "no — use the package" is a feature; for a low-code buyer it can read as not-ready. That buyer is not the ICP.

### Messaging

Connector *count* is a low-code metric. **In code, every API is already connected.** We ship a few protocol connectors that kill the boilerplate every integration repeats; for everything else you use the package you'd already reach for — nothing to wait for, nothing to lock into.

## Definitions

| Layer | Purpose | Owned By | Examples |
|-------|---------|----------|----------|
| SDK | Runtime contract between user code and the platform | Platform | `IIntegration`, `IIntegrationContext`, secrets, logger, execution metadata, payload |
| Connectors | Reusable helpers for talking to external systems | Platform and community | HTTP/API, SQL, SFTP/files, object storage, notifications |
| Trigger adapters | Producers that turn external events into work items | Platform and community | Cron, manual run, webhook, queue message, file arrival, database change |
| Integrations | Customer-specific business logic | Customer/developer | Sync orders, reconcile payments, export invoices |

## SDK

The SDK answers: "How does my code plug into the platform?"

It should stay small and stable. Today it provides:

- `IIntegration` — the entry point the runtime agent executes.
- `IIntegrationContext` — execution-scoped secrets, logging, HTTP client, metadata, and trigger payload.
- Attributes — `[Integration]`, `[ScheduledIntegration]`, `[WebhookIntegration]` declare integration and trigger intent; `[RequiresAgentCapabilities("...")]` declares the agent capabilities an integration needs so it only runs where it can.

The SDK should not become a large catalog of vendor-specific APIs. A small SDK keeps package compatibility manageable and keeps integration authors close to ordinary C#.

## Connectors

Connectors answer: "How do I reliably talk to common systems?"

They are optional libraries that integration authors can compose inside `RunAsync`. They should make common integration chores boring, observable, retry-aware, and consistent. The set is **protocol-bounded** and governed by the litmus test above — we add transports, not vendors.

Status today:

- **HTTP/API connector** *(shipped, sets the bar)* — bearer / API-key-header / API-key-query / basic auth (all secret-key-by-reference), retry with `Retry-After` + exponential backoff + idempotency-key gating on writes, cursor and offset pagination, and query-secret redaction in logs.
- **SQL connector** *(shipped, multi-provider)* — connection from a secret-stored connection string against **SQL Server, PostgreSQL, MySQL, or Oracle**; query/command helpers via Dapper, validation up front, structured logging.
- **SFTP/file, object storage, queue, notification** *(candidates)* — each must clear the litmus test and meet the HTTP connector's bar before it ships.
- **SFTP/file connector** — list, download, upload, move, archive/error folders, checksums, idempotency keys.
- **Object storage connector** — S3/Azure Blob/GCS list, upload, download, metadata, lease/etag handling.
- **Notification connector** — email, Slack, Teams, webhook alerts with execution-aware logging.

Connectors should use SDK primitives instead of replacing them. For example, a connector should accept or derive from `IIntegrationContext` so it can use platform secrets, logger, execution metadata, and cancellation.

## Trigger Adapters

Trigger adapters answer: "What creates work?"

They run outside customer integration code and normalize events into `WorkItem` records. Scheduled, manual, and webhook are the first built-in adapters. Future queue, file-arrival, database-change, dependency, dataset, and API-event adapters should follow the same model:

```
Trigger adapter -> WorkItem -> Agent claim -> ExecutionRecord -> Integration code
```

Trigger adapters should not introduce trigger-specific execution APIs. The runtime agent should continue to execute claimed work items without knowing which adapter produced them.

Work is claimed by environment and, when an integration declares required capabilities, by capability: an agent only claims a work item whose integration's required tags are a subset of the tags that agent offers. With no required tags, claiming is purely by environment as before.

The product direction is to store trigger configuration separately from executable integration metadata. An integration is the code and run policy; trigger records are the schedules, webhooks, queues, file arrivals, API events, or other producers that can create work for that integration. This lets one integration have multiple triggers while preserving the same work-item execution path.

The control plane adapter framework has three pieces:

- `ITriggerAdapter` declares adapter metadata such as source, stored-trigger requirement, payload support, and deduplication support.
- `ITriggerAdapterCatalog` exposes scheduled, manual, webhook, queue, and file adapter descriptors.
- `ITriggerWorkItemProducer` accepts a normalized `TriggerWorkItemRequest` and writes the pending `WorkItem`.

Queue/file implementations should add listener-specific validation and credentials, then use the shared producer rather than creating a new agent execution API.

## Integrations

Integrations answer: "What business outcome should happen?"

An integration should be ordinary C# that composes:

- SDK runtime context.
- Connectors for common system operations.
- Customer-specific mapping, validation, and orchestration logic.

Example shape:

```csharp
public sealed class SyncOrdersIntegration : IIntegration
{
    public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        var api = context.HttpConnector("https://api.shopify.example.com")
            .WithBearerToken("SHOPIFY_TOKEN");                  // secret key, not the value
        var sql = context.PostgresConnector("ERP_CONN");        // or SqlServerConnector / OracleConnector / MySqlConnector

        var orders = await api.GetAllPagesAsync<OrdersPage, Order>(
            "/orders", page => page.Orders, page => page.NextLink, ct);

        foreach (var order in orders)
            await sql.ExecuteAsync(
                "INSERT INTO orders (id, total) VALUES (@Id, @Total)",
                new { order.Id, order.Total }, ct);
    }
}
```

## Design Rules

- Keep the SDK minimal and stable.
- Put reusable external-system behavior in connectors, not in every integration.
- Put event detection and work creation in trigger adapters, not in the runtime execution path.
- Keep integrations as business-specific composition code.
- Every connector operation should support cancellation, logging, and explicit error classification.
- Connector libraries should be versioned independently when practical.
