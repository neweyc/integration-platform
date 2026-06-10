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

Before it runs, `serto test` performs the same structural preflight the control plane enforces at deploy (a discovery attribute is present, a scheduled cron is valid, the class is parameterless-constructible) and adds a few authoring nudges as warnings:

- A required secret referenced in code is missing from `--secrets`.
- A **webhook** integration is being tested without a `--payload`, or the supplied payload is not valid JSON — both exercise a path real deliveries won't.
- `RunAsync` never references its `CancellationToken`, so long-running work won't stop when the platform cancels a run. (This is a source-level check; the compiled type can't reveal whether the token is honored.)

Warnings never block the local run; only the structural errors do.

To replay a signed webhook payload against the control plane without configuring an external sender:

```bash
SERTO_WEBHOOK_SECRET=whs_... serto webhook replay \
  http://localhost:5000/webhooks/acme/order-sync/hook \
  --payload '{"id":123}'
```

Use `--payload-file ./sample-webhook.json` for larger samples and `--delivery-id` when you need to test idempotency behavior with a stable delivery id. The replay command signs the payload with the same `X-Integration-Signature`, `X-Integration-Timestamp`, and `X-Integration-Delivery` headers expected by production webhook delivery.

To run the **whole webhook path locally** — no control plane, no network — add `--local`:

```bash
serto webhook replay --local --payload '{"id":123}'
serto webhook replay --local OrderHook --payload '{"id":123}' --secrets ./secrets.json
```

In local mode the command signs the payload, validates the signed delivery exactly as the control plane would (signature check plus the same timestamp freshness window), and then runs the integration's `RunAsync` with the payload through the same harness as `serto test`. It builds the project first and, with no class name, runs the first integration it finds; pass a class name to target a specific one and `--secrets` to supply integration secrets. The signing secret defaults to a fixed local value (loopback both signs and verifies, so it never leaves the machine), or pass `--secret` to use your own.

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

By default the package is provisioned into the tenant's default environment. Pass `--environment <name>` to target a specific one:

```bash
serto deploy --environment staging
```

The named environment must already exist (deploying into a phantom environment would silently strand the integrations); an unknown name is rejected. The integrations are created or updated in that environment, and the secret check below runs against *its* configured secrets.

The deploy also sends the required secret names found by the scan, and the Control Plane compares them against the secrets configured in the provisioning environment. After upload, the result includes a **secret check** listing which required secrets are configured and which are missing. Missing secrets are reported as a warning only — they do not block the deploy — but any integration that needs an unset secret will fail until you add it (via the Secrets UI or `PUT /api/secrets/{environment}/{key}`).

Use `[Integration]` for executable metadata without stored triggers. Use `[ScheduledIntegration]` and `[WebhookIntegration]` when the package should also provision trigger records. Timeout and retry properties are optional integer named arguments; leave them unset to use the platform defaults.

### Targeting specific agents (capabilities)

By default any runtime agent in an integration's environment can run it. When an integration needs a *particular* host — one wired to hardware, inside a specific network, or with a licensed driver — declare the capabilities it requires:

```csharp
[Integration("Pulse the reactor", "reactor-pulse")]
[RequiresAgentCapabilities("hardware-signal", "site-floor-1")]
public class ReactorPulse : IIntegration { ... }
```

The control plane only routes the integration's work to an agent whose offered tags include **all** of the required tags. An agent advertises what it offers via its config:

```json
{ "Agent": { "Environment": "production", "Tags": ["hardware-signal", "site-floor-1"] } }
```

Notes:

- No `[RequiresAgentCapabilities]` ⇒ runnable on any agent in the environment (unchanged behavior).
- Tags are matched case-insensitively as a set (order doesn't matter).
- Like trigger cron/enabled, the attribute is the **declared default**: an operator can override an integration's required tags in the control plane, and package redeploys preserve that override and report it as drift.
- Capability tags are **routing only** — they decide *where* work runs, not *who* can access what. They are self-reported by the agent and are not a security boundary.
- If no agent offers the required tags, the integration's work stays queued until one connects, rather than running on the wrong host.

`serto scan` lists each integration's required capabilities as an advisory (it runs offline, so it can't check live agents). `serto deploy` goes further: after uploading it checks the control plane and warns if a just-deployed integration is **not currently routable** — i.e. no connected agent in its environment offers the required capabilities.

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
