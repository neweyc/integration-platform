# Writing integrations

This guide explains how to author, test, and deploy integrations using the **Integration-as-Code** workflow.

---

## The Workflow

The `serto` CLI is the primary tool for development.

1. **Initialize:** `serto init MyProject` scaffolds a new C# project.
2. **Develop:** Write your logic in C#. Use attributes like `[ScheduledIntegration]` to define infrastructure.
3. **Test Locally:** `serto dev` watches for changes and runs your integration instantly.
4. **Scan:** `serto scan` previews what package upload will discover and validate.
5. **Package:** `serto package` builds, validates, archives, and hashes the integration package.
6. **Deploy:** `serto deploy` shows the same preview, uploads the package, and auto-provisions your integration in the Control Plane.

---

## Concept

An integration is a C# class that implements `IIntegration`. The class contains your business logic; the platform handles everything else: trigger intake, scheduling, secret injection, execution, logging, and retry.

```csharp
using Serto.Sdk;
using Serto.Connectors.Http;
using Serto.Connectors.Sql;

[ScheduledIntegration(
    "Sync Shopify Orders",
    "shopify-sync",
    "0 * * * *",
    TimeoutSeconds = 300,
    RetryMaxAttempts = 2,
    RetryBackoffSeconds = 60)]
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

Use the `serto dev` command for a high-velocity feedback loop.

```bash
serto dev
```

This command:
- Watches your `.cs` files for changes.
- Automatically rebuilds the project.
- Executes the integration in a local test harness.
- Streams logs directly to your terminal.

To test a specific class or provide a mock webhook payload:

```bash
serto test MyIntegration --payload '{"id": 123}'
```

To replay a signed webhook payload against the control plane without configuring an external sender:

```bash
SERTO_WEBHOOK_SECRET=whs_... serto webhook replay \
  http://localhost:5000/webhooks/acme/order-sync/hook \
  --payload '{"id":123}'
```

Use `--payload-file ./sample-webhook.json` for larger samples and `--delivery-id` when you need to test idempotency behavior with a stable delivery id. The replay command signs the payload with the same `X-Integration-Signature`, `X-Integration-Timestamp`, and `X-Integration-Delivery` headers expected by production webhook delivery.

To preview what the control plane will discover before deploy:

```bash
serto scan
```

The scan builds the project, inspects the compiled assemblies for decorated `IIntegration` classes, validates discovered trigger metadata such as cron expressions, and prints the package name, version, class names, trigger declarations, run policy, and required secret names discovered from connector/context usage. Use `--no-build` to scan the existing `bin` output after a normal build.

To create a deployable archive without uploading it:

```bash
serto package --name MyCompany.Integrations --version 1.0.0
```

The package command runs the same scan preview, writes a `.zip` archive, and prints its SHA-256 hash.

### Package version resolution

The version stamped on a package (and shown per-execution in history) is resolved in this order:

1. An explicit `--version` on `serto package`/`serto deploy`.
2. `<PackageVersion>` in the project file.
3. `<Version>` in the project file.
4. An auto-generated **calendar version** when none of the above is set.

The auto version is a readable, sortable UTC timestamp, `yyyy.MM.dd.HHmmss` (e.g. `2026.06.08.143052`). When the project sits in a git working tree, it is enriched with the commit's short SHA, and a `-dirty` suffix when there are uncommitted changes under the project directory:

```
2026.06.08.143052            # not a git repo (or git unavailable)
2026.06.08.143052-a1b2c3d    # clean tree at commit a1b2c3d
2026.06.08.143052-a1b2c3d-dirty   # uncommitted changes present
```

Git is never required — outside a repository the timestamp alone is used, and git failures never block a build. For repeatable, meaningful versions in CI, pass an explicit `--version` (or set `<Version>`).

---

## Deployment (Zero-Touch Provisioning)

When you are ready to go live, use the `deploy` command.

```bash
SERTO_API_TOKEN=pat_... serto deploy --url http://your-control-plane
```

`serto deploy` publishes the project, creates the archive, runs the scan preview, prints the package hash, and uploads the package only if validation passes. The Control Plane will scan your assembly, discover your classes decorated with integration and trigger attributes, and automatically create or update the executable integration plus its trigger records. This keeps one integration class able to support multiple triggers, such as scheduled and webhook entry points, without duplicating the integration code.

The deploy also sends the required secret names found by the scan, and the Control Plane compares them against the secrets configured in the provisioning environment. After upload, the result includes a **secret check** listing which required secrets are configured and which are missing. Missing secrets are reported as a warning only — they do not block the deploy — but any integration that needs an unset secret will fail until you add it (via the Secrets UI or `PUT /api/secrets/{environment}/{key}`).

Use `[Integration]` for executable metadata without stored triggers. Use `[ScheduledIntegration]` and `[WebhookIntegration]` when the package should also provision trigger records. Timeout and retry properties are optional integer named arguments; leave them unset to use the platform defaults.

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

The platform provides built-in connectors to simplify common integration tasks. To use them, reference the `Serto.Connectors` assembly.

### HTTP Connector

The HTTP connector provides a fluent API for JSON-based API calls with secret-backed authentication, execution-aware logging, retries, rate-limit handling, idempotency headers, and pagination helpers.

```csharp
var client = context.HttpConnector("https://api.example.com")
                    .WithBearerToken("MY_SECRET_KEY")          // or WithApiKeyHeader / WithApiKeyQuery / WithBasicAuth
                    .WithHeader("X-Custom", "value")
                    .WithRetryPolicy(maxRetries: 3)
                    .WithIdempotencyKey(context.Execution.ExecutionId.ToString());

var data = await client.GetJsonAsync<MyData>("/path", ct);
```

For paged APIs, use `GetAllPagesAsync` for cursor/next-link style APIs or `GetOffsetPagesAsync` for offset/limit APIs. HTTP connector secret references are detected by `serto scan` and included in deploy secret checks.

Retries (on `429` and `5xx`, honoring `Retry-After`) apply automatically to idempotent verbs. Non-idempotent writes (`POST`, `PATCH`) are **only** retried when you set `WithIdempotencyKey(...)`, so a retried write can't duplicate a side effect the server may already have applied. Secrets passed as query-parameter API keys (`WithApiKeyQuery`) are redacted from execution logs.

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
  ├── MyIntegration.csproj   # References Serto.Sdk
  ├── MyIntegration.cs       # Your logic with [ScheduledIntegration]
  └── .gitignore
```

### One integration per project (recommended)

Integrations are versioned at the **package** level: a package is one project's `dotnet build`, and
all of the integration classes inside it share a single active version — activating a version moves
every integration in that package together. You *can* put several integration classes in one project
(e.g. a shared-helpers package), and the platform supports it, but then those integrations can only
ever be rolled forward/back as a group, and the Packages page surfaces that grouping.

If you want each integration to version and roll back independently, **keep one integration class per
project** so each gets its own package. This is the simplest and recommended layout; reach for a
multi-integration package only when the integrations genuinely belong to one deployable unit.
