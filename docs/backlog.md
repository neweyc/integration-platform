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

**Status:** In Progress

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
- Added CLI and control-plane tests for scan metadata, invalid cron validation, required-secret detection, package hash calculation, upload provisioning reports, deploy trigger detail formatting, and webhook replay signing/payload handling.

Remaining gaps:

- Deploy reports server-side package provisioning, but it does not yet compare required secret names against the target environment's configured secrets.
- `serto test` does not yet validate cancellation-token usage, connector configuration, sample payload behavior, or required secrets.
- `serto webhook replay` sends signed payloads to a running control plane; it does not yet spin up an in-process control-plane test harness.

### Trigger Declarations And Runtime Overrides

**Status:** Todo

Separate trigger intent in code from operational authority in the control plane. Developers should declare which triggers an integration supports and provide local/default values, while the control plane owns production enablement and environment-specific runtime settings.

Acceptance criteria:

- SDK trigger attributes represent declarations/defaults, not absolute production authority.
- The model can distinguish code-declared defaults from active runtime values, such as declared cron versus production cron override.
- Package upload creates missing trigger records but preserves operator-owned settings including enabled state, production cron override, webhook secret, queue/file bindings, rate limits, and environment-specific configuration.
- UI shows drift when code defaults change while operational overrides remain active.
- Operators can apply a new code default, keep the current override, disable a trigger, or promote settings between environments.
- `serto scan` and `serto deploy` clearly show which trigger fields are declared by code, which are controlled by the control plane, and which will be preserved.
- Tests cover code default changes, preserved production overrides, preserved webhook secrets, disabled trigger preservation, and drift reporting.

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

- Implemented `HttpApiConnector` with fluent API, JSON support, and Bearer token from secrets.
- Implemented `SqlConnector` with Dapper support for queries and commands.
- Added extension methods to `IIntegrationContext` for easy connector access.
- Updated documentation and added NuGet metadata to projects.

Remaining limitations:

- HTTP connector does not yet cover pagination, rate-limit handling, retry classification, or idempotency helpers.
- SQL connector does not yet cover batching, transactions, or bulk upsert patterns.
- File/SFTP, object storage, and notification connectors are not implemented yet.
- Connector behavior needs dedicated tests with fake transports or local test services.

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

### Failed Execution Alerts

**Status:** Todo

Notify operators when integrations fail.

Acceptance criteria:

- Alert destination can be configured per tenant or environment.
- Email or webhook delivery is supported.
- Retry exhaustion can trigger a distinct alert.
- Alert attempts are recorded.

---

## P1 — Integration Package Distribution

### Package Rollback

**Status:** Todo

Allow reverting an integration to a previous package version.

Acceptance criteria:

- UI/API can select a previous package version.
- New executions use the selected version.
- Existing execution history remains tied to the original version.
- Rollback does not require database edits or manual filesystem changes.

### Assembly Isolation And Unload

**Status:** Todo

Improve runtime assembly loading safety.

Acceptance criteria:

- Packages load in isolated `AssemblyLoadContext` instances.
- Old package versions can be unloaded when no executions are using them.
- Dependency conflicts between packages are handled predictably.
- Tests cover two packages with different dependency versions.

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

**Status:** Todo

Add operational health endpoints.

Acceptance criteria:

- `/healthz` reports process liveness.
- `/readyz` checks database connectivity.
- Docker/Kubernetes examples use the endpoints.

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

**Status:** Todo

Avoid forcing users to fully re-login when access tokens expire.

Acceptance criteria:

- Login issues access and refresh tokens.
- Refresh token storage is secure and revocable.
- Logout invalidates refresh token.
- UI handles access-token renewal.

### Rate Limiting

**Status:** Todo

Protect public and agent-facing endpoints from abuse.

Acceptance criteria:

- Login/setup endpoints are rate limited.
- Agent endpoints have reasonable limits per token.
- Rate limit responses are documented.

---

## P2 — Environment Management

### First-Class Environment Model

**Status:** Todo

Replace free-form environment strings with tenant-managed environments.

Acceptance criteria:

- Tenants can create, rename, disable, and delete environments.
- Integrations, secrets, tokens, and agents reference known environments.
- Existing `production`, `staging`, and custom strings migrate cleanly.
- UI has an environment selector.

### Secret Promotion

**Status:** Todo

Support copying secrets between environments.

Acceptance criteria:

- User can copy selected secrets from staging to production.
- Existing target secrets require confirmation before overwrite.
- Secret values remain write-only in user-facing APIs.
- Promotion is audited.

---

## P2 — Developer Experience

### Integration Template

**Status:** Todo

Provide a starter project for integration authors.

Acceptance criteria:

- Template includes a sample `IIntegration`.
- Template includes local unit test examples.
- Template includes publish/package commands.
- Docs reference the template.

### Local Integration Test Harness

**Status:** Todo

Make it easy to run integrations locally with realistic context.

Acceptance criteria:

- CLI or test helper can run one integration class locally.
- Secrets can be loaded from a local JSON/env file.
- Logs are printed to console.
- Cancellation behavior can be tested.

---

## P3 — SaaS And Commercial

### Password Reset

**Status:** Todo

Support forgotten-password flow.

Acceptance criteria:

- Reset request does not reveal whether an email exists.
- Reset token expires.
- Password change invalidates old sessions where applicable.

### Billing Integration

**Status:** In Progress

Integrate subscriptions and payment management.

Acceptance criteria:

- Stripe subscription lifecycle is handled.
- Tenant plan is stored.
- Plan limits are enforced.
- Billing portal link is available to admins.

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

**Status:** Todo

`dotnet ef migrations add` failed locally with a `deps.json` path issue. Migrations were added manually.

Acceptance criteria:

- `dotnet ef migrations add <Name>` works from the repository root.
- Generated migrations compile without manual path fixes.
- Documentation includes the expected migration command.

### Align EF Model Snapshot With Fluent Mapping

**Status:** Todo

Keep the EF fluent model, migrations, and model snapshot consistent for enum string column lengths. The manual-run migration and snapshot use `character varying(20)` for `ExecutionRecord.TriggerSource` and `ManualRunRequest.Status`, but `AppDbContext` currently only applies string conversion without the matching max length.

Acceptance criteria:

- `ExecutionRecord.TriggerSource` fluent mapping includes the same max length as the migration/snapshot.
- `ManualRunRequest.Status` fluent mapping includes the same max length as the migration/snapshot.
- A no-op migration check does not produce type-only churn for these columns.
- Tests/build continue to pass after the mapping cleanup.

### Secrets Page Environment Selector

**Status:** Todo

The secrets UI is still centered around a fixed environment workflow.

Acceptance criteria:

- User can select environment.
- Secret list and mutations use selected environment.
- Agent-token and integration environment choices are consistent.

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
