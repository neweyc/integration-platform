# Functional Operations

How the system works end-to-end from a user and operator perspective.

---

## First-run setup

On first visit, the app detects that no tenant exists (via `GET /api/setup/status`) and redirects to `/setup`. The setup form creates:

1. The first **tenant** (organisation name + slug)
2. The first **admin user** (email + password)
3. Issues a JWT token and logs the user in immediately

The setup endpoint locks itself after first use — any subsequent call returns `409 Conflict`.

---

## Authentication

- Users log in with email and password at `/login`
- The API returns a JWT (24h expiry by default)
- The token is stored in `localStorage` and attached as `Authorization: Bearer <token>` on every API request
- On logout, the token is cleared from `localStorage`
- There is no refresh token mechanism yet — users re-authenticate when the token expires

---

## Tenants

Self-hosted deployments can use the first-run setup flow for a single tenant. SaaS-style deployments can also create tenants through public registration. Every resource carries a `TenantId`, and API queries are expected to scope data by tenant.

The UI is still primarily optimized around the active user's tenant. Cross-tenant administration, billing administration, and tenant lifecycle management are not complete yet.

---

## Environments

Environments (e.g. `production`, `staging`) are a **first-class, per-tenant registry**. Integrations, secrets, agent tokens, and workflows are each scoped to one environment, and an agent token can only read the secrets of its own environment.

- **Canonical names.** Environment names are canonicalized to lowercase and may contain only lowercase letters, numbers, and hyphens. This removes a whole class of silent bugs where `Production` and `production` looked the same but never matched. The runtime agent normalizes its configured environment the same way, so an agent configured as `Production` still loads `production` secrets.
- **Created explicitly.** Environments are managed on the **Environments** page (or `/api/environments`). A write that references an environment which does not exist is rejected with a clear error rather than silently creating a ghost environment from a typo. Every new tenant starts with a single default `production` environment.
- **Lifecycle.** An environment cannot be deleted while live configuration (integrations, secrets, agent tokens, or workflows) still references it; the API returns `409` listing what to move or remove first. Historical records (executions, work items, heartbeats) do not pin an environment.
- **Default.** Exactly one environment per tenant is the default: it is pre-selected when creating integrations and agent tokens and is the target for package auto-provisioning. The default cannot be deleted and its default flag cannot be cleared directly — making another environment the default moves it — so a valid default always exists.

Viewing the registry requires `ViewEnvironments`; creating, editing, or deleting requires `ManageEnvironments`.

---

## Agent capabilities & routing

By default any runtime agent in an integration's environment can run it. When an integration needs a **particular** host — wired to hardware, inside a specific network, with a GPU or a licensed driver — it declares the capabilities it requires, and the control plane only routes its work to an agent that offers all of them.

- **Declared in code, overridable.** Authors declare `[RequiresAgentCapabilities("hardware-signal", …)]`; the value is recorded as the *declared default*. An operator can override an integration's required tags on the Integrations page, and package redeploys preserve that override and report it as drift — the same model as trigger cron/enabled.
- **Agents advertise what they offer.** An agent reports its `Tags` (from config) on every poll and heartbeat. The claim rule is a subset/AND match: an agent claims a work item only when the integration's required tags are all present in the agent's tags. No required tags ⇒ any agent in the environment, unchanged.
- **Unroutable work is surfaced, not silent.** If no live agent in an integration's environment offers its required tags, the work would queue forever — so the Integrations page shows a banner listing the affected integrations and the capabilities they need (`GET /api/integrations/unroutable`).
- **Routing only, not a trust boundary.** Tags decide *where* work runs, not *who* can access what. They are self-reported by the agent; do not rely on them for authorization.

---

## Integrations

An integration is a named, versioned definition of a job. It does not contain code — it is a registry entry that tells the runtime agent what to run, when, and in which environment.

### Fields

| Field | Description |
|-------|-------------|
| Name | Human-readable label |
| Slug | URL-safe identifier, unique within a tenant |
| Description | Optional notes |
| Environment | `production`, `staging`, etc. |
| Trigger type | Built-in values today: `Scheduled`, `Webhook`, or `Manual`; framework descriptors also reserve `Queue` and `File` for listener adapters. |
| Cron expression | Required when trigger is `Scheduled` |
| Status | `Enabled` or `Disabled` |

### Lifecycle

```
Created (Enabled) ──► Disabled ──► Enabled
                 └──► Deleted
```

Disabled integrations are not dispatched to the runtime agent.

### Cron expressions

Five-part cron syntax validated by the Cronos library. Examples:

```
0 * * * *       — every hour
0 9 * * 1-5     — 9am Monday–Friday
*/15 * * * *    — every 15 minutes
0 0 1 * *       — midnight on the 1st of each month
```

---

## Secrets

Secrets are encrypted key/value pairs scoped to a tenant and environment.

### Key format

Keys must match `^[A-Z0-9_]+$` — uppercase letters, numbers, and underscores. This matches the convention used for environment variables, which is how secrets will typically be injected into integration processes.

### Write-only

Secret values are encrypted before storage and **never returned** through the API after being saved. The only way to read values is via the secret bundle endpoint, which requires a valid agent token.

To update a value, call the set endpoint again with the same key — it is an upsert.

### Environments

Secrets are namespaced by environment. The environment must exist in the tenant's [environment registry](#environments) — setting a secret for an unknown environment is rejected. An agent token is scoped to a single environment, so a production agent cannot access staging secrets.

---

## Agent tokens

Agent tokens are service credentials that grant a runtime agent access to the secret bundle for a specific environment.

### Lifecycle

1. Admin creates a token via the UI (name + environment)
2. The plaintext token (`agt_xxx`) is displayed **once** — copy it immediately
3. The token hash is stored in the database
4. The agent includes the token in the `X-Agent-Token` header when calling `/api/agent/secrets/{environment}`
5. The control plane hashes the presented token and looks it up — if found and the environment matches, it returns the decrypted secret bundle
6. To rotate a token: create a new one, update the agent config, then revoke the old one

### Security properties

- Token values are never stored — only the SHA-256 hash
- A compromised token can be revoked instantly from the UI
- Tokens are scoped to a single environment — least privilege by design
- The secret bundle endpoint is not protected by JWT middleware, only by token validation — so it is safe to call from non-browser agents

---

## Secret bundle delivery

When the runtime agent is about to execute an integration, it fetches the secret bundle:

```
GET /api/agent/secrets/production
X-Agent-Token: agt_<token>

200 OK
{
  "secrets": {
    "DATABASE_URL": "postgres://user:pass@host/db",
    "STRIPE_KEY": "sk_live_...",
    "INTERNAL_API_KEY": "abc123"
  }
}
```

The agent injects these as environment variables (or via a typed context object) into the integration's execution environment. Secrets are fetched fresh on each execution — no local caching on the agent.

---

## Execution flow

```
1. Runtime agent loads local integration assemblies from IntegrationsPath
2. Agent polls the control plane for due scheduled integrations in its environment
3. Control plane claims due integrations and advances durable schedule state
4. Agent opens an execution record for each claimed integration
5. Agent fetches the secret bundle for the environment
6. Agent instantiates and runs the integration class with secrets injected
7. Integration logs written through context.Logger are sent to the control plane
8. Agent reports final status: Succeeded / Failed
9. Control plane records the execution result and logs
10. On a terminal failure (no retry remaining), a failure alert is dispatched (see Failure alerts)
```

Scheduling state is stored in `integration_schedule_states` with `last_dispatched_at` and `next_run_at`. New scheduled integrations calculate their first run from the integration creation time. Existing scheduled integrations use the persisted `next_run_at`, so agent restarts do not cause all jobs to run immediately.

Trigger adapters create work items. Due scheduled integrations create claimed scheduled work items, manual run requests create pending manual work items, and signed webhook deliveries create pending webhook work items with payload context. Future queue, file, database, dependency, dataset, or API-event triggers should follow the same producer pattern. Agents claim work items with a 5-minute claim expiry, start executions from claimed work items, and completion mirrors the terminal execution state back to the work item. If an agent crashes after claiming work but before opening an execution record, another agent can reclaim the work item after the claim expires.

If an agent crashes or loses its connection *after* opening an execution record but before reporting a terminal result, the record would otherwise stay `Running` forever and the poll/claim guard would sideline the integration. A control-plane background reaper closes out such orphaned executions: any record running past the integration's configured timeout plus a grace window (or a default ceiling when no timeout is set) is marked `Failed` and its work item released, so dispatch resumes. Thresholds are configurable via the `OrphanedExecutionReaper` settings section.

### Execution history view

Each integration has a full-page history at `/integrations/:id/history` (reached via the **History** action on the Integrations page). It shows a single merged timeline, newest first, combining runtime executions with trigger events. Executions are the primary, selectable rows; trigger events that did not produce a run — `Rejected`, `Deduplicated`, or a `ConvertedToWork` whose work item has not yet executed (shown as **Queued**) — appear as distinct lighter rows, so a webhook that was rejected or work that is stuck waiting for an agent is still visible. Selecting a run shows its logs alongside, with level filtering, message search, and live tailing for in-progress runs. The selected run is reflected in the URL, so a link to a specific execution can be shared.

---

## Failure alerts

Operators can be notified when an integration fails. To keep alerts meaningful, one fires only on a **terminal** failure — an execution that failed (or timed out) and has **no retry remaining**. Transient failures that the retry policy will recover from stay quiet.

**Channels (each optional).** Two delivery channels, configured independently so a tenant can use both, one, or neither:

- **Email** — sent to a list of recipients. By default email goes through the platform mailer (ZeptoMail), so tenants need no email infrastructure. A tenant that wants alerts from its own domain can configure its own SMTP server, which then takes precedence for that tenant. See the [installation guide](installation.md#failure-alert-email-zeptomail) for operator configuration.
- **Outbound webhook** — an HTTP `POST` of a JSON payload to a URL. Works with Slack, Teams, Discord, and PagerDuty incoming webhooks. An optional signing secret adds an `X-Serto-Signature: sha256=…` HMAC header so the receiver can verify authenticity. To prevent SSRF, webhook targets that resolve to private, loopback, link-local, or cloud-metadata addresses are blocked by default — validated both when settings are saved and again at connect time (so DNS rebinding cannot bypass it). Self-hosted operators that deliberately post to internal endpoints can allow this via `AlertWebhooks:AllowPrivateNetworkTargets`.

SMTP servers must use TLS — STARTTLS (typically port 587) or implicit TLS/SSL-on-connect (typically port 465). Plaintext SMTP (port 25, no TLS) is not supported.

**Configuration scope.** Settings live at two levels:

- **Tenant defaults** (the **Alerts** page) — the destinations every integration uses, plus the SMTP server (always tenant-level).
- **Per-integration override** (on the integration's history page) — `Inherit` the tenant defaults, turn alerts `Off` for that integration, or set `Custom` destinations. A custom override still sends email through the tenant's email sender; only recipients and the webhook destination are integration-specific.

A **Send test alert** button at each level delivers a sample through the current configuration so SMTP/webhook setup can be verified immediately.

**Delivery semantics.** Alerting is decoupled from execution recording: the failure is queued and delivered by a background dispatcher in its own scope, so a slow or unreachable mail/webhook endpoint never delays or fails the agent's completion call. Delivery is best-effort — each channel is attempted independently (one failing does not block the other), failures are logged, and there is no automatic retry of the alert itself. Alerts queued when the control plane restarts are not redelivered; the execution remains durably recorded as `Failed` regardless. SMTP passwords and webhook signing secrets are encrypted at rest and never returned by the API.

Permissions: `ViewAlerts` to view configuration, `ManageAlerts` to change it or send a test. Admins and Developers get both; Operators can view.

---

## Integration packages

The control plane can store compiled integration packages as tenant-scoped zip files. A package has:

- Name
- Version
- Original filename
- Size in bytes
- SHA-256 hash
- Zip data

Packages are uploaded through `POST /api/integration-packages` as `multipart/form-data`. The zip must contain at least one `.dll`. The same package name and version cannot be uploaded twice for a tenant.

On upload, the control plane scans package assemblies for concrete `IIntegration` classes decorated with SDK attributes. Discovered integrations are created or updated by slug, pinned to the uploaded package version, and assigned the fully qualified class name from the assembly. Trigger attributes create or update child trigger records separately, so one integration class can be provisioned with scheduled and webhook triggers without duplicating executable integration metadata. Invalid discovered metadata, such as an invalid cron expression, rejects the upload before the package row is stored.

Runtime agents can list and download tenant packages through agent-token endpoints. Downloaded packages are SHA-256 verified, extracted into a per-package directory under the agent's `PackagesPath` (named by package id), and loaded by the runtime agent. When a poll returns an integration pinned to a package the agent does not yet have on disk — for example, immediately after the integration is repointed to a new version — the agent syncs that package on demand rather than waiting for the periodic sync interval. A pinned execution resolves its class only from that package's own directory; if the package is not yet present, the run is skipped and retried once it lands, so a repointed integration never runs the previous version's code.

Each package directory loads into its own `AssemblyLoadContext`. This matters because an integration project's `AssemblyVersion` is rarely bumped per build, so two package versions usually share the same assembly identity; without isolation the runtime would dedupe to the first-loaded version and a repoint would not take effect until the agent restarted. The `Sdk` contract assembly and the logging abstractions are shared with the agent's default context so the `IIntegration`/`IIntegrationContext`/`ILogger` types match across the boundary, while the integration assembly and its private dependencies (connectors, third-party libraries) load in isolation per package. The agent's local `IntegrationsPath` is not versioned and continues to load into the default context. Current limitation: package contexts are non-collectible, so old versions are not yet unloaded and accumulate over a long-running agent's lifetime as integrations are repeatedly redeployed.

Integrations are versioned at the **package** level: a package's integrations all run the same version and move together, never split across versions. This holds because a package is one `dotnet build` — the integration classes in it were compiled and tested as a unit, so mixing versions across them is never a wanted state. A deploy already moves every integration in a package to the freshly uploaded version (each discovered integration is provisioned pointing at it), and the only manual change — **activate** — is package-level too. Activating a version (`PUT /api/integration-packages/{id}/activate`, `ManageIntegrations`) repoints every integration on any version of that package name to the chosen version at once; it takes effect on the next run. An integration whose class is absent from the target version (renamed/removed) is left where it is and reported as *skipped* rather than broken. Execution records retain the package id/name/version used for each run, so history is unaffected.

Implementation note: the active version is still physically the per-integration `Integration.PackageId` pin (the agent dispatch path is unchanged); the package-level guarantee is enforced by every write path — deploy and activate — moving the whole package together, rather than by a schema constraint. There is intentionally no per-integration repoint endpoint.

The integration history page shows the active version and lets an operator pick another version of the same package; choosing one activates it for the whole package. A general integration update never changes the version. The dedicated Packages page groups versions by package name in an accordion with a name filter, marks each version **active** (with the integrations on it) or **stale**, offers one-click **Activate** per version (gated on `ManageIntegrations`), and allows deleting stale versions only — deletion of an in-use version is blocked (`409`) because the pin uses `OnDelete(SetNull)` and would otherwise silently un-pin the integrations; activate another version first. Current operational limitations: packages are not environment-scoped, package deletion does not remove local agent cache entries, and package assemblies are isolated per version but not yet unloaded (old versions accumulate on a long-running agent).

---

## User roles

| Role | Capabilities |
|------|-------------|
| Admin | Full access — manage integrations, secrets, packages, tokens, environments, users, alerts, and billing/admin tenant operations |
| Developer | Deploy and operate integrations, manage secrets, packages, agent tokens, environments, and failure alerts; cannot manage users or billing |
| Operator | View integrations, executions, logs, environments, and alert configuration; trigger manual runs; cannot view secrets or deploy code |
| Member | Legacy read-only role; can view integrations and execution history |

Server-side role enforcement is active through endpoint permission filters. The UI uses the same role/permission model to hide unavailable navigation items and actions. Disallowed direct API calls still receive `403 Forbidden`.

Admins can invite tenant users from the Users page. The page lists active users and pending invitations. The invite response shows a one-time invitation token and browser accept link; the invited user sets their password on the public accept-invitation page. Pending invitations can be resent, which rotates the token and extends expiry, or revoked, which expires the invite immediately. Invite, resend, revoke, and acceptance events are audited.

---

## Audit log

The control plane records tenant-scoped audit entries for security- and configuration-relevant changes, including secrets, integrations, packages, agent tokens, environments, personal access tokens, invitations, and alert configuration (including test sends). Entries include actor, action, target type/id, timestamp, and a value-free summary. Secret values and plaintext tokens are never stored in audit entries. Audit log reads are admin-only through the API and the Audit log page.

---

## Workflow DAGs

Workflow definitions can declare nodes and dependencies, with each node referencing an existing integration in the same environment. Starting a workflow run queues root nodes as workflow work items. When a node execution succeeds, downstream nodes are queued after all dependencies have succeeded. A failed terminal node marks the workflow run failed and blocks downstream nodes. Runtime agents execute workflow work through the same poll/start/complete path as scheduled, manual, webhook, and retry work. Workflow root and downstream dispatches also emit trigger timeline events, so workflow-created work is visible through the same trigger observability view.

The Workflows page lists workflow definitions, can start a workflow run, and shows recent run/node status. Current limitations: workflow authoring is API-first and workflow nodes are integration executions only. Human approvals, waits/signals, branch conditions, and visual editing are future work.

---

## Data retention

No automatic data retention policies are implemented yet. Execution history and logs need a retention policy to prevent unbounded database growth. Recommended approach: keep 90 days of execution records and execution logs, configurable per tenant.
