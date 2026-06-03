# SDK, Connectors, Trigger Adapters, And Integrations

This platform should stay code-first, but code-first should not mean every team rewrites the same HTTP pagination, SQL batching, SFTP file movement, object storage, retry, and logging boilerplate.

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

The SDK should not become a large catalog of vendor-specific APIs. A small SDK keeps package compatibility manageable and keeps integration authors close to ordinary C#.

## Connectors

Connectors answer: "How do I reliably talk to common systems?"

They are optional libraries that integration authors can compose inside `RunAsync`. They should make common integration chores boring, observable, retry-aware, and consistent.

Examples:

- **HTTP/API connector** — authentication helpers, JSON calls, pagination, rate-limit handling, retry classification, response logging.
- **SQL connector** — connection setup from secrets, query/command helpers, batching, transactions, bulk upsert patterns.
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
        var api = HttpApiConnector.FromContext(context, "SHOPIFY");
        var sql = SqlConnector.FromContext(context, "ERP");

        var orders = await api.GetPagedJsonAsync<Order>("/orders", ct);
        await sql.BulkUpsertAsync("orders", orders, ct);
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

