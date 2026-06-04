# Backlog

This backlog tracks implementation work that is not yet complete. It is ordered by practical product value and production risk, not by long-term vision. The roadmap remains the phase-level view; this file is the actionable work queue.

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

1. Workflow DAG foundation.
2. Agent pools and routing.
3. Trigger adapter framework.
4. Core connectors.
5. Code-first transform steps.
6. Environment promotion.
7. Audit and RBAC.
8. Execution token scoping.
9. Retention and quotas.

---

## P0 — MVP Completion

### Workflow DAG Foundation

**Status:** Done

Move the product from single integration runs to graph-based workflow orchestration.

Acceptance criteria:

- Workflow definitions can declare nodes and dependencies.
- Each node references an existing integration in the workflow environment.
- Workflow definitions reject cycles, duplicate node keys, unknown edge references, and cross-environment integrations.
- Starting a workflow run queues root nodes as workflow work items.
- Successful upstream completion queues downstream nodes.
- Fan-in nodes wait for all parents to succeed.
- Failed terminal nodes fail the workflow run and block downstream work.
- Workflow runs expose node status for operational visibility.
- UI lists workflow definitions, starts workflow runs, and shows latest node states.
- Tests cover sequential DAGs, fan-in, failure blocking, and workflow work-item polling.

Completed notes:

- Added `workflow_definitions`, `workflow_nodes`, `workflow_edges`, `workflow_runs`, and `workflow_node_runs`.
- Added workflow context to `work_items` and a new `Workflow` trigger source.
- Added API-first workflow creation, run start, and run-history endpoints.
- Added workflow UI for listing workflows, starting runs, and viewing recent run/node state.
- Runtime agents remain trigger-agnostic: workflow nodes are claimed and executed through the same work-item/start/complete path as other triggers.

### Version-Pinned Package Execution

**Status:** Done

Make package-backed execution deterministic and operable by tying every integration run to an explicit package version. This is the highest-priority product gap because developers should upload packages through the control plane without needing access to agent hosts, and operators need to know exactly what code ran.

Acceptance criteria:

- Integration record can reference a specific package version.
- Create/edit integration UI can select an uploaded package version.
- Agent resolves the integration class from the pinned package, with local `IntegrationsPath` kept as a development fallback.
- Execution record stores package id, package name, and package version used.
- Execution history shows the package version for each run.
- Updating an integration to a new package version affects only future executions.
- Previous execution history remains tied to the original package version.
- Rollback can be performed by repointing the integration to a previous package version.
- Tests cover pinned execution, package version changes, rollback selection, and execution-history version retention.

### Durable Scheduling State

**Status:** Done

Store scheduling state in the control plane instead of the agent's in-memory `_lastRun` dictionary.

Acceptance criteria:

- Each scheduled integration has persisted last-dispatched or last-completed scheduling state.
- Agent restart does not cause every cron integration to re-evaluate from `DateTime.MinValue`.
- Due calculation is deterministic across restarts.
- Execution start is guarded so the same due run cannot be started twice by accident.
- Tests cover restart behavior and duplicate-dispatch prevention.

Completed notes:

- `integration_schedule_states` stores `last_dispatched_at` and `next_run_at`.
- `GET /api/agent/integrations` claims due scheduled integrations and advances state in the control plane.
- The runtime agent no longer stores durable cron state locally.
- Leases prevent duplicate dispatch (see Lease-Based Scheduling Recovery below).

### Lease-Based Scheduling Recovery

**Status:** Done

Add claim leases so abandoned scheduled work can be retried safely.

Acceptance criteria:

- Schedule claim records include lease owner and lease expiry.
- Agent starts execution using a valid claim.
- Expired claims can be reclaimed.
- Duplicate execution is still prevented while a lease is active.
- Tests cover abandoned claims, active claims, and reclaimed claims.

Completed notes:

- Lease ownership moved from `integration_schedule_states` to persisted `work_items`.
- When polling, the control plane claims work items with a 5-minute claim expiry.
- Another agent cannot start work while an active claim exists.
- Expired claims can be reclaimed by any agent on the next poll.
- `POST /api/agent/executions` validates active work-item claim ownership.
- Work items are marked `Completed`, `Failed`, or `TimedOut` when execution completes.
- Tests cover claim state transitions, ownership validation, and expiry behavior.

### Graceful Agent Shutdown

**Status:** Done

Make the runtime agent drain or cancel running work predictably during shutdown.

Acceptance criteria:

- Agent stops polling when cancellation is requested.
- In-flight executions either finish within a configured drain window or are cancelled.
- Cancelled executions are reported to the control plane with a useful failure message.
- Buffered execution logs are flushed before process exit.
- Tests cover shutdown during active execution.

Completed notes:

- `Worker.StopAsync` stops polling, drains in-flight executions for `ShutdownDrainSeconds`, then cancels remaining work.
- The host shutdown timeout is extended to cover the configured drain window plus a buffer.
- Cancelled executions are completed as failed with an agent-shutdown message.
- Execution logs are buffered and flushed before success, failure, or shutdown completion.
- RuntimeAgent tests cover active execution shutdown, drain completion, forced cancellation, and default shutdown options.

### Manual Run Support

**Status:** Done

Add a user-facing way to trigger an integration immediately. This is the first external trigger and the simplest path for testing/debugging integrations without waiting for cron.

Acceptance criteria:

- API endpoint exists to request a manual run for an integration.
- UI exposes a `Run now` action.
- Manual runs create normal execution records and logs.
- Disabled integrations cannot be manually run unless explicitly allowed by design.
- Scheduled and manual executions do not overlap for the same integration.
- Execution history records source as `Manual`.

Completed notes:

- `POST /api/integrations/{id}/run` creates a pending `ManualRunRequest`.
- UI "Run now" button calls this endpoint for any enabled integration.
- `GET /api/agent/integrations` returns pending manual runs alongside due scheduled integrations.
- `TriggerSource` field on `ExecutionRecord` tracks whether a run was Scheduled, Manual, or Webhook.
- Overlap prevention: cannot create a manual run if one is already pending or if the integration is already running.
- `POST /api/agent/executions` validates manual run requests and updates status to Started.

### Work Item Execution Queue

**Status:** Done

Introduce a general persisted work item model so scheduled, manual, webhook, and future event sources all feed the same agent execution path.

Acceptance criteria:

- Add `integration_work_items` or equivalent persisted table.
- Work item stores tenant, integration, environment, trigger source, status, payload, availability time, claim owner, and claim expiry.
- Scheduled polling creates or claims scheduled work through this model.
- Manual run creates a pending manual work item.
- Agents poll/claim work items instead of receiving raw integration definitions.
- Execution records reference the work item or trigger source.
- Tests cover scheduled, manual, claimed, expired-claim, and duplicate-claim behavior.

Notes:

- This should become the long-term dispatch model.
- Durable scheduling state can remain the producer of scheduled work items.
- This unlocks webhook and queue/event triggers without creating separate execution paths.

Completed notes:

- Added persisted `work_items` table with tenant, integration, environment, trigger source, status, availability, claim owner, claim expiry, manual-run link, and payload fields.
- Scheduled polling creates and claims scheduled work items instead of returning raw schedule claims.
- Manual run requests create pending manual work items that agents claim through the same poll path.
- Agent execution start now validates an active work-item claim and records `WorkItemId` on the execution record.
- Execution completion mirrors terminal state back to the work item.
- Expired manual and scheduled claims can be reclaimed.
- Tests cover claim ownership helpers, scheduled and manual claiming, expired-claim reclaim, running-execution guards, agent API start/complete flow, and runtime-agent work-item execution.

Remaining limitations:

- Future event triggers are not implemented yet. Webhook triggers now use the work-item payload and trigger-source fields.

### Webhook Trigger Support

**Status:** Done

Allow external systems to trigger integrations through tenant/integration-specific webhook endpoints.

Acceptance criteria:

- Integration can be configured with `Webhook` trigger type.
- Control plane exposes a stable webhook URL for each webhook integration.
- Webhook request creates a pending work item for an agent.
- Request payload is stored or referenced safely and passed to the integration context.
- Webhook executions appear in history with source `Webhook`.
- Basic authentication option exists, such as shared secret header or signature verification.
- Tests cover valid webhook, invalid auth, disabled integration, wrong environment, and payload handoff.

Notes:

- The control plane should receive and validate webhooks, but the runtime agent should still execute the integration.

Completed notes:

- Webhook integrations get a generated shared secret and stable `/webhooks/{tenantSlug}/{integrationSlug}` URL.
- Webhook delivery verifies `X-Integration-Signature` as an HMAC-SHA256 signature over `{X-Integration-Timestamp}.{raw request body}`.
- `X-Integration-Timestamp` enforces a 5-minute replay window.
- Optional `X-Integration-Delivery` provides idempotency and prevents duplicate work item creation per webhook integration.
- Valid webhook requests create pending webhook work items with payload and delivery ID.
- Agent polling claims webhook work items through the same work-item queue path as scheduled and manual runs.
- Runtime agent passes webhook payload into `IIntegrationContext.Payload`.
- Execution records use `TriggerSource.Webhook` because execution start reads the trigger from the claimed work item.
- Tests cover valid webhook delivery, invalid signature, stale timestamps, disabled integration, duplicate delivery IDs, integration-scoped delivery IDs, environment-scoped claiming, and payload handoff.

Remaining limitations:

- Public webhook rate limiting is not implemented yet.
- Delivery forensics are intentionally thin; stored delivery records do not yet include request IP, user agent, signature fingerprint, or payload hash.

---

## P0 — Prime Time Readiness (Funding & Launch)

### Developer API Tokens & UI

**Status:** Done

Provide a seamless "One-Click" deployment experience for the CLI.

Acceptance criteria:
- UI has a "Developer" tab.
- Users can generate a personal API token for use with `ip deploy`.
- Tokens are securely hashed in the database.
- One-click "Copy Command" for `ip login` or `ip deploy`.

Completed notes:
- Implemented `UserToken` (PAT) system with `pat_` prefix.
- Created `UserTokenAuthenticationMiddleware` for seamless integration.
- Added `/api/user-tokens` endpoints.

### "Killer" Sample Project Gallery

**Status:** Done

Demonstrate the "Aha!" moment with real-world examples.

Acceptance criteria:
- A `src/Examples` directory containing 3-5 high-value integrations (e.g., Salesforce to SQL, Stripe to Slack).
- Each example is fully documented and uses `Integration-as-Code` attributes.
- Ready to be "Copied and Pasted" by new users.

Completed notes:
- Created `StripeToSlack`, `SqlToHttp`, and `SalesforceSync` examples in `src/Examples`.

### `ip dev` Hot-Reload Loop

**Status:** Done

Create a habit-forming developer feedback loop.

Acceptance criteria:
- `ip dev` command watches local source files for changes.
- Automatically triggers a project build and `ip test` on file save.
- Clear, color-coded console output for success/failure.

Completed notes:
- Implemented `ip dev` command using `FileSystemWatcher`.
- Integrated with `TestCommand` for automatic execution on change.

---

## P0 — Valuation & Governance Multipliers

### Audit Log Infrastructure

**Status:** Done

Implement an immutable record of all administrative actions to satisfy enterprise procurement requirements.

Acceptance criteria:
- Every secret create/update/delete is recorded. ✓ (values never stored)
- Every integration deployment is recorded. ✓ (create/update/delete)
- Every user invitation/acceptance is recorded. ✓
- Audit entries include Actor, Timestamp, Action, and Target. ✓

Completed notes:
- `audit_log` table; `AuditLogEntry` records tenant, actor (id + denormalized email), action, target type/id, value-free summary, and timestamp.
- Auditing is cross-cutting: commands implement `IAuditableCommand.Describe(result)`, and an `AuditingDispatcher` decorator records after the command succeeds — handlers and their unit tests are untouched. (AcceptInvitation records explicitly since the actor is the new, not-yet-authenticated user.)
- Covers Secret set/delete, Integration create/update/delete, AgentToken create/revoke, personal access token create/revoke, Package upload/delete, User invite/accept.
- Audit writes never break the primary operation (failures are logged, not thrown). Secret values and token plaintext are never recorded.
- `GET /api/audit-log` is gated by a new `ViewAuditLog` permission (Admin only).
- Admin UI includes an Audit log page that lists recent entries and is hidden from roles without `ViewAuditLog`.
- Tests: per-command descriptor unit tests (incl. explicit no-secret-value assertions) + end-to-end integration tests proving entries are recorded with correct actor/action/target, no secret leakage, and Admin-only access.

### Role-Based Access Control (RBAC)

**Status:** Done

Separate administrative power from developer activity.

Acceptance criteria:
- Roles: `Admin`, `Developer`, `Operator`. (`Member` retained as legacy read-only.)
- `Developer` can deploy and test, but not manage billing or invitations.
- `Operator` can view logs and trigger manual runs, but not view secrets or deploy code.

Completed notes:
- `Permission` enum + `RolePermissions` matrix in `Infrastructure/Authorization` is the single source of truth for access policy.
- `.RequirePermission(Permission)` endpoint filter enforces server-side: 401 if unauthenticated, 403 (`ForbiddenException` → ProblemDetails) if the role lacks the permission.
- Applied across Integrations, Secrets, Packages, Agent Tokens, Invitations, direct user registration, and Tenant/billing endpoints. Agent (`X-Agent-Token`), Setup, login, tenant self-registration, and Webhook endpoints are intentionally outside the user-role model.
- Fixed a latent claim-mapping bug: JWT `role` claim is remapped to `ClaimTypes.Role` on validation; `CurrentUser.Role` now reads either form.
- Tests: full `RolePermissions` matrix unit tests + endpoint allow/deny integration tests per role (Operator/Developer/Member/Admin) using the real invite → accept → enforce flow.

---

## P0 — The "Magic" Experience

### Attribute-Based Discovery

**Status:** Done

Allow developers to define integration metadata directly in code.

Acceptance criteria:
- `[Integration]`, `[ScheduledIntegration]`, and `[WebhookIntegration]` attributes exist in the SDK.
- Attributes support setting name, slug, cron expression, timeout, and retry settings.
- Documentation updated with attribute examples.

### Assembly Scanning & Auto-Provisioning

**Status:** Todo

Automatically create or update integration records when a package is uploaded.

Acceptance criteria:
- Control plane scans uploaded assemblies for types decorated with integration attributes.
- New integrations are auto-created in the database.
- Existing integrations are updated if their attributes have changed (e.g., new cron).
- Typos in class names are eliminated by deriving them directly from the type.

### `ip` CLI

**Status:** Todo

The primary entry point for developers.

Acceptance criteria:
- `ip init` scaffolds a new integration project.
- `ip dev` runs a local agent with hot-reload.
- `ip deploy` packages and uploads to the control plane.
- CLI is cross-platform (Windows, macOS, Linux).

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

### Retry Policy

**Status:** Done

Add configurable retry behavior for failed integrations.

Acceptance criteria:

- Integration has retry count and backoff settings.
- Failed executions can create retry attempts.
- Execution history shows original attempt and retries clearly.
- Retry exhaustion produces a final failed state.
- Tests cover retry success, retry exhaustion, and non-retryable cancellation.

Completed notes:

- Integrations can configure `RetryMaxAttempts` and `RetryBackoffSeconds`.
- Failed retryable executions create a delayed retry work item using the same agent claim path as scheduled, manual, and webhook work.
- Retry work items carry `AttemptNumber`, `ParentExecutionId`, and `RootExecutionId` so execution history can distinguish initial attempts from retries.
- Retry exhaustion leaves the final attempt failed without queuing more work.
- Agent shutdown cancellation is reported as non-retryable, so graceful shutdown does not create retry loops.
- Tests cover retry creation, retry exhaustion, non-retryable cancellation, retry polling, and agent API retry queueing.

### Execution Timeouts

**Status:** Done

Prevent integrations from running forever.

Acceptance criteria:

- Integration execution has a configurable timeout.
- Timeout cancels the integration through `CancellationToken`.
- Timeout is recorded distinctly in execution history.
- Logs include timeout context.

Completed notes:

- Integrations can configure `TimeoutSeconds` through API and UI.
- Runtime agent links each execution token to the configured timeout and cancels timed-out runs.
- Timed-out executions are reported to the control plane with status `TimedOut` and a timeout error message.
- Timeout values are validated to be greater than zero when provided.
- Tests cover timeout reporting, no-timeout execution, timeout validation, list response round-tripping, and cancellation that is not caused by timeout.

Limitations:

- Timeout enforcement depends on integration code honoring the provided `CancellationToken`.

### Scheduled Claim Running-Execution Guard

**Status:** Done

Prevent scheduled polling from consuming a due schedule when the integration already has a running execution. The start path rejects overlap, and the poll path also avoids creating or claiming work while an execution is active.

Acceptance criteria:

- `GET /api/agent/integrations` does not claim or advance scheduled work for integrations with an active running execution.
- Schedule state is not advanced when no execution can start.
- Existing work-item claim recovery behavior still works for abandoned scheduled claims.
- Tests cover scheduled poll behavior when an execution is already running.

Completed notes:

- `PollRepository.ClaimDueScheduledAsync` now checks active running executions before schedule evaluation.
- Due scheduled integrations with running executions are skipped without creating or advancing schedule state.
- Once the running execution completes, the next poll can claim the due integration normally.
- Integration tests cover skipping a running integration, preserving its schedule state, and claiming it after completion.

### Manual Run Claim Failure Handling

**Status:** Todo

Handle manual run claims that never reach execution start, such as when an agent cannot resolve the integration class or fails before calling `POST /api/agent/executions`.

Acceptance criteria:

- Manual run requests that repeatedly fail before start move to a terminal failed or expired state.
- Users can request a new manual run after the previous request has expired or failed.
- Control plane records a useful failure reason when the agent reports a pre-start failure.
- Agent reports pre-start failures for missing integration classes where possible.
- Tests cover missing class, expired claim, reclaim, and new request after terminal failure.

### Agent Heartbeats

**Status:** Done

Track runtime agent presence and health in the control plane.

Acceptance criteria:

- Agent periodically reports heartbeat, environment, version, hostname, and current concurrency.
- Control plane stores last-seen timestamp.
- UI shows active/stale agents.
- Agent token revocation is reflected in heartbeat failure behavior.

Completed notes:

- Runtime agents send heartbeat data from the poll loop with environment, assembly version, hostname, current concurrency, and max concurrency.
- The control plane upserts heartbeat state per tenant and agent token.
- User API can list heartbeats and marks agents stale after two minutes without a heartbeat.
- Agent-token revocation naturally prevents future heartbeat posts because heartbeat uses the same `X-Agent-Token` validation path.
- Tests cover heartbeat upsert, stale detection, and API post/list behavior.

Remaining limitations:

- There is no UI health page yet.
- Heartbeats do not yet drive dispatch routing, pool membership, or capacity-aware claim assignment.

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

### Agent Package Sync

**Status:** Done

Connect control-plane package storage to runtime agents.

Acceptance criteria:

- Agent can list or fetch packages authorized for its tenant/environment.
- Package download verifies SHA-256 before activation.
- Package is extracted into an agent-managed directory.
- Agent can detect new package versions without manual copy.
- Package activation does not corrupt currently running executions.

Completed notes:

- Agent-facing package list/download endpoints are available under `/api/agent/packages` and authenticated with `X-Agent-Token`.
- `PackageSyncer` lists packages, downloads missing versions, verifies SHA-256, extracts to `PackagesPath`, and loads extracted assemblies.
- Package extraction uses a temporary directory before activation to avoid partially written package directories.
- RuntimeAgent tests cover download/extract, hash mismatch, already-synced packages, control-plane errors, and per-package download failures.
- ControlPlane integration tests cover agent-token package list and download endpoints.

Remaining limitations:

- Packages are tenant-scoped, not environment-scoped.
- Integration definitions are not pinned to a package/version yet.
- Loaded assemblies are still in the default load context and cannot be unloaded.
- Package deletion in the control plane does not remove local agent cache entries.

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

### Audit Log

**Status:** Done

Record security- and configuration-relevant actions.

Acceptance criteria:

- Secret create/update/delete events are audited without secret values. ✓
- Integration create/update/delete events are audited. ✓
- Agent token create/revoke events are audited. ✓
- Personal access token create/revoke events are audited without plaintext values. ✓
- Package upload/delete events are audited. ✓
- Audit entries include actor, tenant, timestamp, action, and target id. ✓

See "Audit Log Infrastructure" above for implementation notes.

### Role Enforcement

**Status:** Done

Make user roles meaningful.

Acceptance criteria:

- Admin-only operations are enforced server-side. ✓ (see RBAC above)
- Member role has read-only access where appropriate. ✓
- UI hides unavailable actions for non-admin users. ✓
- Tests cover authorization failures. ✓

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

### SDK NuGet Package

**Status:** Done

Publish `IntegrationPlatform.Sdk` as a package.

Acceptance criteria:

- Package metadata is complete.
- Versioning strategy is documented.
- Example integration project consumes the package.
- Compatibility with runtime agent versions is documented.

Completed notes:

- Added NuGet metadata (version, authors, description, etc.) to `Sdk.csproj` and `Connectors.csproj`.

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

### Tenant Self-Registration

**Status:** Done

Allow public sign-up for SaaS mode.

Acceptance criteria:

- New tenant and first admin can self-register.
- Duplicate tenant slugs are handled.
- Email verification is supported or explicitly deferred.

Completed notes:

- Implemented `RegisterTenant` feature and endpoint.
- Creates both tenant and initial admin user in one atomic operation.

### User Invitations

**Status:** Done

Allow tenant admins to invite users.

Acceptance criteria:

- Admin can invite by email.
- Invite token expires.
- Invite acceptance creates user in correct tenant.
- Invite events are audited.
- Admin UI can create an invite and expose an accept link.
- Public accept-invitation UI lets the invited user set a password.
- Admin UI lists active users and pending invitations.
- Admin UI can revoke and resend pending invitations.

Completed notes:

- Implemented `Invitation` domain model and feature.
- Secure token generation and public accept endpoint.
- Added role-gated Users UI for tenant admins to create invitations.
- Added public accept-invitation UI.
- Added Admin-only `GET /api/auth/users` and `GET /api/invitations` endpoints.
- Added Admin-only invitation revoke/resend endpoints. Resend rotates the token and extends expiry; revoke expires the invite immediately.

### Password Reset

**Status:** Todo

Support forgotten-password flow.

Acceptance criteria:

- Reset request does not reveal whether an email exists.
- Reset token expires.
- Password change invalidates old sessions where applicable.

### Usage Metering

**Status:** Done

Track billable usage.

Acceptance criteria:

- Executions are counted per tenant and billing period.
- Package storage usage can be measured.
- Usage API supports billing dashboard.

Completed notes:

- Implemented `QuotaService` to track and enforce monthly execution limits.
- Added `MaxExecutionsPerMonth` to `Tenant` model.
- `StartExecutionHandler` now enforces the quota before starting a run.

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

### Scheduled Claim Test Stability

**Status:** Done

Keep scheduled polling integration tests deterministic regardless of the current UTC time.

Completed notes:

- `PollRepositoryIntegrationTests.ClaimDueScheduledAsync_AcquiresLeaseAndPersistsScheduleState` now pins the integration `CreatedAt` before the test's fixed `now`.
- This prevents new `* * * * *` schedules from being evaluated as not due when the test runs after noon UTC.

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
