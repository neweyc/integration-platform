# Backlog

This backlog tracks implementation work that is not yet complete. It is ordered by practical product value and production risk, not by long-term vision. The roadmap remains the phase-level view; this file is the actionable work queue.

Completed and verified work has been archived to [backlog-completed.md](backlog-completed.md) to keep this queue focused on pending work.

Status key:

- `Todo` — not started
- `In Progress` — partially implemented or actively being built
- `Blocked` — needs a decision or prerequisite
- `Done` — completed and verified

---

## Product Direction

Build a code-first workflow automation and integration platform that can replace the common 60-70% of Control-M/Boomi usage: scheduled jobs, data movement, API calls, transformations, retries, observability, and environment-safe deployment.

The product should stay code-first. Developers should author real integrations and workflow logic with normal language tooling, tests, packages, and CI/CD. The UI should focus on control, observability, operations, administration, and safe deployment rather than becoming the primary authoring surface.

Principles:

- Real code over low-code lock-in.
- Versioned package deployment and rollback over copying files to agents.
- Work items and workflows over one-off execution paths.
- Trigger adapters over trigger-specific execution paths.
- Agent execution close to systems and data.
- Operations-grade visibility, retry, audit, and access control.
- Built-in primitives for the common integration work: schedules, HTTP/API calls, database access, files/SFTP, transformations, secrets, alerts, and environment promotion.

Priority ladder:

1. Developer authoring loop.
2. Trigger declarations and runtime overrides.
3. AI-assisted integration authoring.
4. Agent pools and routing.
5. Trigger adapter framework.
6. Core connectors.
7. Code-first transform steps.
8. Environment promotion.
9. Execution token scoping.
10. Retention and quotas.

---

## P0 — The "Magic" Experience

### Developer Authoring Loop

**Status:** Done

All acceptance criteria are met: scan/package/deploy previews and provisioning reports, the `serto test` preflight (attributes, cron, discoverability, connector config, cancellation-token usage, sample-payload handling), required-secret detection and the deploy secret check (now honoring `--environment`), and signed webhook replay both against a running control plane and fully offline via `--local`. One beyond-scope nice-to-have remains in the gaps list (payload *schema* deserialization).

Make the normal developer path seamless before layering AI or MCP on top of it. A developer should be able to create, test, scan, package, deploy, and inspect an integration without guessing what the control plane will do.

Acceptance criteria:

- `serto scan` performs local assembly scanning and shows discovered integrations, trigger declarations, package metadata, class names, and validation errors before upload.
- `serto package` builds the package archive, verifies DLL/dependency contents, calculates SHA-256, and runs the same scan preview.
- `serto deploy` reports the package version, integrations created or updated, trigger records created or preserved, webhook URLs, missing secrets, and next scheduled runs.
- `serto test` validates attributes, cron expressions, class discoverability, connector configuration, cancellation-token usage where feasible, and sample payload handling.
- Local webhook replay lets developers run a signed sample payload without waiting on an external sender.
- A generated or scanned secret manifest identifies required secret names without storing values in code.
- Tests cover scan preview, package validation, deploy result reporting, missing secret detection, and webhook replay.

Completed notes:

- Added `serto scan` to build and inspect local integration assemblies before upload.
- Added `serto package` to publish, archive, validate, calculate SHA-256, and print the same scan preview without uploading.
- Updated `serto deploy` to run the package/scan preview before upload and cancel when scan validation fails.
- Updated package upload to return a server-side provisioning report, including created/updated integrations, trigger actions, webhook URLs, preserved webhook-secret status, pinned package id, and computed next scheduled run.
- Updated `serto deploy` to render the server-side provisioning report after upload.
- Added `serto webhook replay` to sign and POST sample webhook payloads with production-compatible HMAC headers, timestamp, and delivery id.
- Added source-based required-secret detection for direct `context.Secrets`, `TryGetValue`, `WithBearerToken`, and `SqlConnector` usage.
- `serto deploy` sends the scanned required-secret names with the upload; the control plane compares them against the secrets configured in the provisioning environment and returns a secret check (required/satisfied/missing) that the CLI renders. Advisory only — a missing secret does not block the upload.
- Added CLI and control-plane tests for scan metadata, invalid cron validation, required-secret detection, package hash calculation, upload provisioning reports, deploy trigger detail formatting, webhook replay signing/payload handling, and the deploy secret check (handler comparison plus end-to-end upload).

Completed notes (continued):

- `serto deploy --environment <name>` is now honored end to end: the CLI sends the target environment with the upload, and the control plane provisions the integrations into it and runs the secret check against its configured secrets. The named environment must already exist (an unknown name is rejected); omitting the flag still falls back to the tenant's default environment. Covered by handler tests (explicit env, unknown env rejected, blank-falls-back-to-default) and an end-to-end upload test.
- `serto test` preflight now also warns when `RunAsync` never references its `CancellationToken` (a source-level check, since the compiled type can't reveal token threading), when a webhook integration is tested without `--payload`, and when a supplied payload is not valid JSON. Added unit tests for the cancellation-token detector and the payload/webhook preflight warnings.
- `serto webhook replay --local` runs the whole webhook path offline: it signs the payload, validates the signed delivery exactly as the control plane would (signature + the same freshness window), then runs the integration's `RunAsync` with the payload through the `serto test` harness — no running control plane required. The CLI mirrors the server signature/freshness contract (as it already does for the webhook header names), with the tolerance constant kept in step and the verify/freshness helpers locked by unit tests.

Remaining gaps:

- `serto test` preflight still does not validate sample-payload *schema* behavior beyond "is it JSON" (it does not deserialize against an expected shape). The connectors validate their configuration at construction (absolute http(s) base URL, parseable SQL connection string).

### Trigger Declarations And Runtime Overrides

**Status:** In Progress

Separate trigger intent in code from operational authority in the control plane. Developers should declare which triggers an integration supports and provide local/default values, while the control plane owns production enablement and environment-specific runtime settings.

Acceptance criteria:

- SDK trigger attributes represent declarations/defaults, not absolute production authority.
- The model can distinguish code-declared defaults from active runtime values, such as declared cron versus production cron override.
- Package upload creates missing trigger records but preserves operator-owned settings including enabled state, production cron override, webhook secret, queue/file bindings, rate limits, and environment-specific configuration.
- UI shows drift when code defaults change while operational overrides remain active.
- Operators can apply a new code default, keep the current override, disable a trigger, or promote settings between environments.
- `serto scan` and `serto deploy` clearly show which trigger fields are declared by code, which are controlled by the control plane, and which will be preserved.
- Tests cover code default changes, preserved production overrides, preserved webhook secrets, disabled trigger preservation, and drift reporting.

Completed notes:

- `IntegrationTrigger` now stores code-declared defaults (`DeclaredCronExpression`, `DeclaredEnabled`) alongside the active `CronExpression`/`Enabled`. Divergence between active and declared is treated as an operator override.
- Trigger reconcile is split into code-driven (package upload) and operator-driven (UI/API update) modes. Code-driven redeploys record the new declared defaults and preserve operator overrides of enabled-state and cron (webhook-secret preservation already existed); operator updates set active values, and divergence from the declared default becomes the override.
- The provisioning report, `serto deploy` output, and the integration list/get API expose per-trigger `cronOverridden`/`enabledOverridden` and the declared cron, so preserved overrides surface as drift. The integrations UI shows a "drift" badge with a tooltip explaining the override.
- Tests cover disabled-trigger preservation, cron-override preservation with the new declared default recorded, follow-code-when-not-overridden, and operator-change-then-redeploy-preserves; plus provisioning-report drift mapping and CLI drift formatting.

Remaining gaps:

- Operator actions are not built yet: apply-the-new-code-default (clear an override), and promote settings between environments.
- Queue/file bindings, rate limits, and environment-specific configuration are not modeled as declared-vs-override yet (only enabled-state and cron are).
- The drift UI is read-only (badge + tooltip); there is no reconcile control.

### AI-Assisted Integration Authoring

**Status:** Todo

Let developers describe an integration in natural language and receive compiling, testable, platform-conformant code. AI should accelerate authoring without bypassing validation, secrets discipline, or human deployment approval.

Acceptance criteria:

- `ip ai new "<intent>"` generates an integration class, SDK attributes, connector usage, tests, sample payloads, and a required-secret manifest.
- `ip ai add-trigger`, `ip ai add-connector`, and `ip ai test` modify existing projects through normal files rather than opaque platform state.
- Generated code uses `IIntegrationContext`, structured logging, cancellation tokens, connector APIs, secret references by name, and retry/idempotency-aware patterns.
- Generated output never inlines secret values and warns when a requested workflow implies unsafe non-idempotent writes.
- AI generation runs local validation (`serto test` and `serto scan`) before deploy.
- Failed execution logs can feed an `ip ai fix --from-last-run` workflow that proposes a patch and tests it locally before deployment.
- Tests cover prompt-to-code generation contracts, secret-manifest generation, sample payload generation, validation failure handling, and failure-log diagnosis workflows.

### MCP / Agent-Operable Control Plane

**Status:** Todo (Phase 2 — sequence after authoring UX is seamless)

Expose the control-plane lifecycle — author, deploy, run, observe — as a Model Context Protocol (MCP) server so any MCP-compatible AI client (Claude Desktop, Claude Code, IDE assistants, agent frameworks) can operate the platform conversationally. This completes the AI-authoring story: an agent that can write an integration should also be able to deploy it, trigger a run, and read the resulting logs through one governed interface, rather than handing off to a human at the UI.

Why this fits this product specifically:

- The hard, dangerous parts are already built — a clean command/dispatcher model, RBAC (`RequirePermission`), agent/user tokens, and an immutable audit log. That trio is exactly what makes agent-driven infrastructure actions safe, and most products that bolt on MCP lack it. Here, MCP is a thin, well-governed adapter, not a from-scratch build.
- It is the natural completion of AI-assisted authoring (see Developer Experience): "AI can write integrations" becomes "AI can operate the platform."
- MCP is becoming the standard AI-tooling interface, so being MCP-native is a go-to-market wedge for a code-first, AI-friendly platform.

Core design rule (non-negotiable):

- MCP tools MUST flow through the same command + permission + audit pipeline as the REST API. No parallel implementation. Ideally generate the MCP tool schemas from the existing command metadata so they cannot drift.

Acceptance criteria:

- The first MCP milestone is read-mostly and validation-oriented: list integrations, inspect packages, read execution logs, read trigger events, inspect missing secrets, and validate scan/package results.
- Later write-capable MCP tools can expose operations such as `create_integration`, `deploy_package`, `run_integration`, and `create_workflow` only after the authoring loop and permission model are mature.
- Every tool call is authenticated and authorized via the existing user/agent token + RBAC, and recorded in the audit log with the acting principal.
- Tools are tiered: a read-only tier (list/inspect) is separable from a write tier (create/deploy/run).
- Secrets are write-only over MCP — set/rotate is allowed, reading a secret value is never exposed.
- Destructive or production-affecting tools (deploy, run, delete) surface enough context for an agent/human to confirm intent.
- Tenancy isolation holds: a token scoped to one tenant cannot read or act across tenants.
- Tests cover tool authorization failures, tenant isolation, secret write-only enforcement, and audit linkage.

Notes and cautions:

- This is a surface, not new capability — its value scales with how much users actually drive the platform via AI agents. Build it once the authoring loop (connectors, local test harness, AI generation) is genuinely seamless, so agents are operating a mature surface.
- Blast radius is real: an agent that can create integrations, manage secrets, and trigger runs needs deliberate guardrails (the tiering, write-only secrets, and confirmation points above). The audit log is the after-the-fact backstop, not the only control.
- Distinct, more speculative idea to keep separate: integrations *consuming* external MCP servers as a connector type. That overlaps the existing connector model without a clear near-term win; park it.

---

## P1 — Runtime Reliability

### Trigger Adapter Framework

**Status:** Todo

Make "anything can trigger a job" a first-class architecture rule. Scheduled, manual, and webhook triggers already prove the model: each trigger source should detect or receive an event, validate it, normalize payload and metadata, and enqueue a `WorkItem`. The runtime agent and execution APIs must remain trigger-agnostic.

Acceptance criteria:

- A trigger adapter contract exists for producing work items from different trigger sources.
- Scheduled, manual, and webhook producers are documented as the first built-in adapters.
- Future trigger sources can be added without changing the agent execution path.
- Work item payload and metadata can carry normalized trigger-specific context.
- Trigger-source observability records when a trigger was received, accepted, deduplicated, rejected, or converted to work.
- Tests cover at least one new adapter using the shared work-item producer path.

Candidate adapters:

- Queue/event bus: SQS, Azure Service Bus, RabbitMQ, Kafka.
- File/object arrival: SFTP, local watched folders, S3/Azure Blob/GCS.
- Database: polling queries, change tables, CDC streams.
- API event: authenticated enqueue endpoint distinct from webhook integrations.
- Workflow dependency: upstream job completion creates downstream work.
- Dataset availability: partition/table/object readiness creates work.

Design rule:

- Triggers produce `WorkItem`; agents execute `WorkItem`; integration code receives normalized context through `IIntegrationContext`.

Completed notes:

- Added `ITriggerAdapter`, `ITriggerAdapterCatalog`, and built-in descriptors for scheduled, manual, webhook, queue, and file adapters.
- Added `GET /api/trigger-adapters` so UI/tooling can discover adapter capabilities instead of hardcoding them.
- Added `ITriggerWorkItemProducer` as the shared path from normalized trigger events to pending `WorkItem` records.
- Manual run and webhook delivery now enqueue work through the shared producer.
- Added Queue/File trigger source and trigger type enum values without changing the runtime agent execution contract.
- Queue/File work items are claimable through the standard agent poll path once a listener produces them.
- Added generic `trigger_events` observability for received, accepted, deduplicated, rejected, failed, and converted trigger events.
- Added `GET /api/trigger-events` for operator visibility across trigger adapters.
- Webhook delivery now records both webhook-specific delivery rows and generic trigger events.
- Shared work-item production records `ConvertedToWork` and `Deduplicated` trigger events for future adapters.
- Scheduled polling, workflow root/downstream dispatch, and retry scheduling now also record `ConvertedToWork` trigger events, so the trigger timeline covers all built-in work producers instead of only push-style triggers.
- Added tests for adapter catalog discovery and a queue-style adapter using the shared producer path.

### Core Connectors

**Status:** In Progress

Provide reusable code-first helpers for common external-system work without bloating the SDK runtime contract.

Acceptance criteria:

- Connector boundary is documented separately from SDK and trigger adapters.
- HTTP/API connector supports auth from secrets, JSON requests, pagination, retry classification, and rate-limit handling.
- SQL connector supports connections from secrets, query/command helpers, transactions, batching, and bulk upsert patterns.
- File/SFTP connector supports list, download, upload, move/archive/error folders, checksums, and idempotency keys.
- Object storage connector supports S3/Azure Blob/GCS list, upload, download, metadata, and etag/lease handling.
- Notification connector supports email, Slack, Teams, and generic webhook notifications.
- Connector operations accept cancellation and emit execution-aware logs.
- Tests cover connector behavior with fake transports or local test services.

Completed notes:

- Implemented `HttpApiConnector` with fluent API, JSON verbs, bearer/header/query/basic auth from secrets, idempotency headers, retry/rate-limit handling, normalized HTTP errors, and next-link/offset pagination helpers.
- Implemented `SqlConnector` with Dapper support for queries and commands.
- Added extension methods to `IIntegrationContext` for easy connector access.
- Updated documentation and added NuGet metadata to projects.

Remaining limitations:

- HTTP connector does not yet cover pagination, rate-limit handling, retry classification, or idempotency helpers.
- SQL connector does not yet cover batching, transactions, or bulk upsert patterns.
- File/SFTP, object storage, and notification connectors are not implemented yet.
- Both connectors now validate their configuration at construction (the HTTP base URL must be an absolute http(s) URL; the SQL connection string must parse), so misconfiguration fails fast during `serto test` instead of on the first call. Missing auth secrets still surface at first use (HTTP) or at construction (SQL).

Design rule:

- SDK defines how code runs; connectors define reusable ways to talk to systems; trigger adapters define how work is created; integrations compose these pieces into business-specific logic.

### Manual Run Claim Failure Handling

**Status:** Todo

Handle manual run claims that never reach execution start, such as when an agent cannot resolve the integration class or fails before calling `POST /api/agent/executions`.

Acceptance criteria:

- Manual run requests that repeatedly fail before start move to a terminal failed or expired state.
- Users can request a new manual run after the previous request has expired or failed.
- Control plane records a useful failure reason when the agent reports a pre-start failure.
- Agent reports pre-start failures for missing integration classes where possible.
- Tests cover missing class, expired claim, reclaim, and new request after terminal failure.

### Agent Version Reporting

**Status:** In Progress

Report agent version and SDK compatibility to the control plane.

Acceptance criteria:

- Runtime agent reports version during heartbeat or poll.
- Control plane records version per agent.
- UI shows version and stale/unsupported state.
- Compatibility checks can warn when packages require a newer agent.

Completed notes:

- Runtime agents report their assembly version as part of heartbeat.
- The control plane stores the reported version on the agent heartbeat record.

Remaining limitations:

- SDK/package compatibility requirements are not modeled yet.
- UI does not yet show unsupported or stale-version warnings.

### Agent Capability Tags

**Status:** Done

Route work to agents by capability, not just environment, so integrations that need a specific
host (hardware access, a VPN, a GPU, a licensed driver) only run where they can. Design doc:
[docs/agent-capability-tags.md](agent-capability-tags.md).

Scope is **tags only** — multi-environment agents and treating tags as a trust/security boundary
are explicit non-goals (each needs its own design; tags here are routing-only and self-reported).

Acceptance criteria:

- Integrations declare required capabilities in code via a `[RequiresAgentCapabilities(...)]` SDK
  attribute, discovered by the scanner and stored on the integration.
- Required tags follow the declared-default + override pattern (operator override preserved across
  redeploys, drift reported) like trigger cron/enabled.
- Agents advertise offered tags (`AgentOptions.Tags`), sent on poll and reported on heartbeat.
- The claim path only routes a work item to an agent when `integration.RequiredTags ⊆ agent.Tags`
  (subset/AND); no required tags ⇒ any agent (today's behavior preserved).
- "Unroutable" work — pending items no live agent in the environment can satisfy — is surfaced in
  the UI and as a failure-alert candidate, never a silent stall.
- Backward compatible: existing agents/integrations/tokens unchanged after migration.

Notes:

- Forward-compatible with a later multi-environment-agent change (eligibility already reads
  `env ∈ agent.environments`).
- If a capability ever needs to be *trusted*, tags must move from self-reported to server-assigned
  on the agent token — folds into the "Authz Revisit" pass.

Completed notes:

- `[RequiresAgentCapabilities("...")]` SDK attribute; scanner extracts and normalizes tags onto the
  integration (`RequiredTags` + `DeclaredRequiredTags`, Postgres `text[]`), with the same
  declared-default/override/drift handling as trigger cron/enabled.
- Agents advertise `AgentOptions.Tags` via an `X-Agent-Capabilities` header on poll and heartbeat;
  `PollRepository` only claims a work item when `TagSet.IsSatisfiedBy(integration.RequiredTags, agentTags)`.
- `GET /api/integrations/unroutable` + an Integrations-page banner surface work no live agent can run.
  (Wiring it into the failure-alert channel was left as a future add — currently UI-only.)
- Operators can override required tags on the integration edit form; preserved across redeploys.
- Backward compatible (empty tags ⇒ any agent). Tests across scanner, upload drift, routing, unroutable
  detection, operator override, and a DB-backed array-persistence/reconcile test. Docs updated.

### Execution Token Scoping

**Status:** Todo

Issue a short-lived execution token when an agent starts a run. The agent uses this token for execution-scoped operations, such as log streaming and completion, instead of using the broad agent token for the entire run.

Acceptance criteria:

- `POST /api/agent/executions` returns an `executionToken` with the `executionId`.
- Execution token is scoped to exactly one execution record.
- Execution token expires after a short configurable window.
- Log writes require the execution token.
- Execution completion requires the execution token or validates both the agent token and execution token.
- Stored execution tokens are hashed, not stored as plaintext.
- Failed or expired execution-token use returns consistent `401` or `403` responses.
- Tests cover wrong execution id, expired token, revoked parent token, and cross-environment attempts.

Notes:

- Revoking the parent agent token should prevent future execution starts.
- Decide whether parent token revocation should also invalidate active execution tokens.
- This will reduce the blast radius of a leaked agent token during long-running executions.

---

## P1 — Logging And Observability

### Batched Execution Log Transport

**Status:** Todo

Replace one-HTTP-request-per-log-call with batching and reliable flush behavior.

Acceptance criteria:

- Agent buffers log events per execution.
- Logs flush in batches during execution.
- Logs flush on success, failure, and shutdown.
- Failed log submission uses bounded retry/backoff.
- Large log volume cannot block integration execution indefinitely.

### Log Volume Controls

**Status:** Todo

Limit log size and database growth.

Acceptance criteria:

- Maximum message length and exception length are enforced consistently.
- Maximum log events per execution is configurable.
- Truncated logs are marked clearly.
- Retention cleanup includes execution logs.

### Log Filtering UI

**Status:** Todo

Improve execution log navigation in the UI.

Acceptance criteria:

- Logs can be filtered by level.
- Logs can be searched by message text.
- Long exceptions can expand/collapse.
- Running executions can refresh logs.

### Metrics Dashboard

**Status:** Todo

Add a control-plane dashboard for operational health.

Acceptance criteria:

- Shows recent executions.
- Shows success/failure rate.
- Shows average and p95 duration.
- Shows current agent status.
- Can filter by environment.

---

## P1 — Integration Package Distribution

### Assembly Isolation And Unload

**Status:** In Progress

Improve runtime assembly loading safety.

Acceptance criteria:

- Packages load in isolated `AssemblyLoadContext` instances.
- Old package versions can be unloaded when no executions are using them.
- Dependency conflicts between packages are handled predictably.
- Tests cover two packages with different dependency versions.

Completed notes:

- Each downloaded package directory now loads into its own non-collectible `AssemblyLoadContext` (`IntegrationLoader.LoadPackage`). Two package versions that share the same assembly identity (the default `AssemblyVersion` `1.0.0.0`, rarely bumped per build) coexist in one process, so repointing an integration to a new package takes effect without an agent restart. Previously the runtime deduped by identity and the first-loaded version won, so a repoint silently kept running the original code.
- The `Sdk` contract assembly and `Microsoft.Extensions.Logging.Abstractions` are shared with the default context so `IIntegration`/`IIntegrationContext`/`ILogger` identities match across the agent/integration boundary; everything else (the integration assembly, connectors, third-party libraries) loads privately per package.
- The agent's local `IntegrationsPath` continues to load into the default context (single, unversioned set) — only versioned package directories are isolated.
- Two related load bugs were fixed alongside isolation: a missing (not-yet-synced) package directory no longer poisons the load-path cache, and a pinned integration whose package is unavailable returns null (skip + retry) instead of falling back to a stale version from the global pool. The agent also syncs a package on demand when a poll references one it does not have on disk, rather than waiting for the periodic interval.
- Tests cover same-class-from-two-directories producing distinct coexisting `Type` identities, the missing-then-created path, and the no-stale-fallback behavior.

Remaining gaps:

- Package contexts are non-collectible, so old versions are not unloaded and accumulate over a long-running agent's lifetime as integrations are repeatedly redeployed. Collectible contexts plus unload-when-idle (no in-flight executions on the old version) are still to do.
- Dependency-version conflict behavior across packages is isolated by construction but not yet explicitly tested.
- The shared-contract match relies on the SDK assembly being named `Sdk`; giving it an explicit, less generic assembly name would make the sharing rule more robust.

---

## P1 — API And Data Growth

### Pagination

**Status:** Todo

Add pagination to list endpoints that can grow without bound.

Acceptance criteria:

- Integrations, secrets, packages, execution history, logs, and tokens support pagination where appropriate.
- API responses include stable continuation or page metadata.
- UI handles paging or incremental loading.
- Existing unpaginated behavior is migrated without breaking current screens.

### Execution Retention

**Status:** Todo

Prevent execution records and logs from growing forever.

Acceptance criteria:

- Retention duration is configurable.
- Cleanup job deletes old execution logs and records safely.
- Defaults are documented.
- Cleanup activity is logged.

### Health Checks

**Status:** Done

Add operational health endpoints.

Acceptance criteria:

- `/healthz` reports process liveness.
- `/readyz` checks database connectivity.
- Docker/Kubernetes examples use the endpoints.

Completed notes:

- Added a Health feature folder (`Features/Health/HealthEndpoints.cs`) exposing anonymous `GET /healthz` and `GET /readyz`, mapped via the standard `MapHealthEndpoints()` convention.
- `/healthz` reports liveness only (no dependency checks) so a transient database outage can't cause an orchestrator to kill a healthy process; `/readyz` pings the database via `CanConnectAsync` and returns `503 {"status":"not-ready","database":"down"}` when unreachable.
- Control-plane Dockerfile installs `curl` and adds a `HEALTHCHECK` on `/healthz`, inherited by every image (dev, trial, prod).
- Build-from-source `docker-compose.yml` adds a `/readyz` healthcheck so a `healthy` control plane means it can actually reach the database.
- Documented both endpoints in `installation.md` with Docker and Kubernetes liveness/readiness probe examples.
- Added `HealthEndpointsIntegrationTests` covering anonymous `/healthz` and database-backed `/readyz`.

### Cache Derived Encryption Key

**Status:** Todo

Avoid running PBKDF2 for every secret encrypt/decrypt operation. `AesEncryptionService` currently derives the AES key on each call, which makes secret bundle decryption unnecessarily expensive when an environment has many secrets.

Acceptance criteria:

- AES key is derived once per process/configured master key.
- `GetSecretBundle` decrypting many secrets does not perform PBKDF2 once per secret.
- Random IV per encryption remains unchanged.
- Tests verify repeated encrypt/decrypt calls work with the cached key.
- Future key rotation requirements are documented separately.

Notes:

- Current service registration is scoped; a singleton key provider or immutable cached key is preferable.
- Do not cache plaintext secret values.

---

## P2 — Security And Administration

### Agent Token Expiry And Rotation

**Status:** Todo

Make agent credentials easier to rotate safely.

Acceptance criteria:

- Agent tokens can have expiry timestamps.
- UI shows token age and expiry.
- Rotation flow supports overlapping old/new tokens.
- Expired tokens are rejected by all agent endpoints.

### JWT Refresh Tokens

**Status:** Done

Avoid forcing users to fully re-login when access tokens expire.

Acceptance criteria:

- Login issues access and refresh tokens.
- Refresh token storage is secure and revocable.
- Logout invalidates refresh token.
- UI handles access-token renewal.

Completed notes:

- Added a `RefreshToken` entity (only the SHA-256 hash stored) and `IAuthTokenIssuer`, which issues an access + refresh pair on every sign-in flow: login, first-run setup, and invitation acceptance.
- `POST /api/auth/refresh` rotates the token (old one revoked, successor recorded); reuse of a revoked token revokes the whole chain (theft detection). `POST /api/auth/logout` revokes a token idempotently.
- Refresh lifetime is `Jwt:RefreshTokenExpiryDays` (default 30).
- The frontend stores both tokens and transparently refreshes once on a 401, replaying the original request; a single in-flight refresh is shared across concurrent 401s. Sign-out revokes server-side, best-effort.

### Rate Limiting

**Status:** Done

Protect public and agent-facing endpoints from abuse.

Acceptance criteria:

- Login/setup endpoints are rate limited.
- Agent endpoints have reasonable limits per token.
- Rate limit responses are documented.

Completed notes:

- Added ASP.NET Core rate limiting: a generous global per-IP fixed window across the API, plus a strict `auth` policy (default 10/60s per IP) on login, setup, password reset, and token refresh.
- Static assets bypass the limiter (served earlier in the pipeline) and health endpoints opt out so monitoring is never throttled. Rejections return `429` with `Retry-After` and a problem body.
- Fully config-driven via the `RateLimit` section (documented in `installation.md`); tests disable it via `RateLimit:Enabled=false`.
- Agent endpoints are covered by the global per-IP limit. Per-agent-token limits remain a future refinement (noted, not yet implemented).

### Trusted Agent Capability Tags

**Status:** Todo (low priority)

Today agent capability tags (see [Agent Capability Tags](#agent-capability-tags)) are **self-reported by
the agent and used for routing only** — they decide *where* work runs, not *who* can access what. The
moment a tag needs to *mean* something security-wise (e.g. `pci-host`, a data-residency region, a
compliance boundary), self-reported tags are worthless: an agent could simply claim the tag. Such tags
must instead be **server-assigned to the agent token** and trusted from there.

Low priority — only needed when a real trusted-capability requirement appears; it should be designed as
part of the broader [Authz Revisit](#authz-revisit) pass rather than bolted on piecemeal.

Acceptance criteria:

- A subset of capability tags can be **granted on the agent token** (server-side), not just self-reported.
- Routing distinguishes trusted (token-granted) tags from self-reported ones; an integration can require a trusted tag.
- An agent cannot self-grant a trusted tag by reporting it in `X-Agent-Capabilities`.
- The distinction is visible in the UI (which tags are trusted vs advertised).

---

## P2 — Environment Management

### Secret Promotion

**Status:** Todo

Support copying secrets between environments.

Acceptance criteria:

- User can copy selected secrets from staging to production.
- Existing target secrets require confirmation before overwrite.
- Secret values remain write-only in user-facing APIs.
- Promotion is audited.

---

## P3 — SaaS And Commercial

### Password Reset

**Status:** Done

Support forgotten-password flow.

Acceptance criteria:

- Reset request does not reveal whether an email exists.
- Reset token expires.
- Password change invalidates old sessions where applicable.

Completed notes:

- `POST /api/auth/forgot-password` always returns 204 (no user enumeration); when the email matches a user it stores a hashed, 1-hour reset token and emails a link via the platform-default ZeptoMail sender.
- `POST /api/auth/reset-password` consumes the single-use token, sets the new BCrypt password, and revokes all of the user's refresh tokens so existing sessions can't outlive the reset.
- Reset links use `App:BaseUrl`; without an email sender or base URL configured the request still succeeds and logs a clear operator warning.
- Frontend: `/forgot-password` and `/reset-password` pages plus a "Forgot your password?" link on sign-in.

### Billing Integration

**Status:** Done

Integrate subscriptions and payment management.

Acceptance criteria:

- Stripe subscription lifecycle is handled.
- Tenant plan is stored.
- Plan limits are enforced.
- Billing portal link is available to admins.

Completed notes:

- Added a Billing feature behind `IStripeGateway` (the only type touching the Stripe SDK), so handlers are SDK-free and unit-testable. Inert unless `Stripe:SecretKey` is configured.
- `POST /api/billing/webhook` verifies the signature and reconciles tenant `Plan`, `SubscriptionStatus`, Stripe ids, and `MaxExecutionsPerMonth` from `checkout.session.completed` and `customer.subscription.created|updated|deleted` (the webhook is the source of truth). Plan limits flow into the existing `IQuotaService`.
- `Tenant.Plan` (Free/Team/Business/Enterprise) and `SubscriptionStatus` added with a migration; quotas: Free 1k, Team 10k, Business 100k.
- Admins (`ManageBilling`) get `GET /api/billing/current` (plan + usage), `POST /api/billing/checkout` (Stripe Checkout), and `POST /api/billing/portal` (Stripe Billing Portal). Frontend Billing page with usage bar, plan switcher, and "Manage billing".
- 13 tests (webhook reconciliation, checkout/portal guards, status). Configuration documented in `installation.md`.

### Commercial Licensing (Community edition + license key)

**Status:** Todo

Give self-hosted deployments an upgrade path: a free Community edition (the full product, capped by
estate size) and a signed commercial license that lifts the caps for paying businesses. Design doc:
[docs/licensing.md](licensing.md).

Core principles: cap, don't cripple (trial is representative); gate self-hosted on **integrations +
environments**, not executions (metering stays cloud-only); the license is a compliance instrument,
not DRM.

Acceptance criteria:

- `BillingPlanCatalog` gains `MaxIntegrationsFor(plan)` (Community = 5); enforced on `CreateIntegration`
  **and** package-upload provisioning, blocking only net-new integrations beyond the cap (redeploys of
  existing ones always succeed). Environments cap (2) already enforced.
- The self-hosted hard execution cap is relaxed (execution metering remains cloud-only).
- A signed, offline license file (`{ licensee, plan, expiry }`) is validated at startup against a
  shipped public key and sets the deployment's `Plan` — the on-prem analog of the Stripe webhook. No
  phone-home; instance-level entitlement.
- Expiry degrades to Community caps after a grace period with warnings — never bricks a running system.
- Edition, expiry, and caps are surfaced in the UI.
- A vendor-side key-issuance tool exists.

Depends on a decision: keep SDK/CLI/Connectors MIT but make the **control plane source-available /
commercial** (not MIT), so unlicensed commercial use is a real liability — see the design doc.

### On-Prem Secret Vault (reference-based secrets)

**Status:** Todo

Stop storing secret *values* in the control plane so a hosted control plane can hold references only —
the prerequisite for a cloud offering under "credentials never leave our network." Design doc:
[docs/secret-vault.md](secret-vault.md); strategy context in [docs/cloud-strategy.md](cloud-strategy.md).

Acceptance criteria:

- An `ISecretBackend` abstraction; today's encrypted-in-DB store becomes the **embedded** backend (no
  behavior change, default for simple self-hosted).
- An **external-vault** backend: the control plane stores `{ environment, key } → reference` only; secret
  values live in an on-prem vault container (first-party or an OpenBao/HashiCorp Vault/KMS integration),
  and the **agent resolves references against the vault at run time** rather than receiving a decrypted
  bundle from the control plane.
- Secret *values* never rest in (ideally never transit) the control plane under the external backend; the
  Secrets UI manages bindings, not values.
- Migration tooling from embedded → external; the control plane no longer needs `Encryption:MasterKey`
  under the external backend.
- Cloud deployments mandate the external backend ("no secrets in the cloud, full stop").

Enables the cloud tier (phase-2); decouples secret storage from the control plane for security-conscious
self-hosted buyers too.

### Marketplace

**Status:** Todo

Support publishing and installing reusable integrations.

Acceptance criteria:

- Public integration metadata model exists.
- Packages can be marked marketplace-visible.
- Tenants can install marketplace packages.
- Trust, signing, and compatibility checks are defined.

---

## Technical Debt

### Fix EF Migration Tooling

**Status:** Done

`dotnet ef migrations add` failed locally with a `deps.json` path issue. Migrations were added manually.

Acceptance criteria:

- `dotnet ef migrations add <Name>` works from the repository root.
- Generated migrations compile without manual path fixes.
- Documentation includes the expected migration command.

Completed notes:

- Added `AppDbContextDesignTimeFactory` (`IDesignTimeDbContextFactory<AppDbContext>`) so the EF tooling builds the context directly instead of booting the web app (whose startup applies migrations and needs full Jwt/DB/encryption config). This is what broke design-time resolution.
- `dotnet ef migrations add <Name> --project src/ControlPlane/ControlPlane.csproj` now generates clean, compiling migrations (the refresh-token and password-reset migrations were generated this way). The command is documented in `installation.md`.

### Align EF Model Snapshot With Fluent Mapping

**Status:** Todo

Keep the EF fluent model, migrations, and model snapshot consistent for enum string column lengths. The manual-run migration and snapshot use `character varying(20)` for `ExecutionRecord.TriggerSource` and `ManualRunRequest.Status`, but `AppDbContext` currently only applies string conversion without the matching max length.

Acceptance criteria:

- `ExecutionRecord.TriggerSource` fluent mapping includes the same max length as the migration/snapshot.
- `ManualRunRequest.Status` fluent mapping includes the same max length as the migration/snapshot.
- A no-op migration check does not produce type-only churn for these columns.
- Tests/build continue to pass after the mapping cleanup.

### Input Sanitization

**Status:** Todo

Validation exists per feature, but there is no consistent sanitization approach.

Acceptance criteria:

- Decide which fields should be trimmed, normalized, or rejected.
- Apply consistently across commands.
- Tests cover normalization behavior.

### API Error Consistency

**Status:** Todo

Ensure all endpoints return consistent Problem Details responses.

Acceptance criteria:

- Validation, conflict, not found, and authorization failures are consistent.
- Agent endpoints do not leak tenant/resource existence across token boundaries.
- API docs match actual error responses.
