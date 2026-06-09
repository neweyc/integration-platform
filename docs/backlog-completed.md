# Backlog — Completed

Completed and verified work, archived from [backlog.md](backlog.md) to keep the active queue readable. Sections retain their original priority grouping and implementation notes. For pending work, see [backlog.md](backlog.md).

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

### Multi-Trigger Integration Model

**Status:** Done

Separate "what code runs" from "what causes it to run." Today an integration has a single `TriggerType`, a single `CronExpression`, and webhook secret fields directly on the integration. That blocks realistic workflows where the same integration should be runnable by schedule, manual action, webhook, queue event, file arrival, API event, or multiple schedules/webhooks.

This was completed before assembly scanning and auto-provisioning so package discovery does not bake in the old one-integration-one-trigger constraint.

Acceptance criteria:

- `Integration` represents executable code, package pinning, environment, status, class name, timeout, and retry policy.
- Trigger configuration moves to a child model such as `IntegrationTrigger`.
- One integration can have zero or more enabled triggers.
- Manual runs remain available as an operator action and do not require a stored trigger unless explicitly modeled later.
- Scheduled trigger configuration supports at least cron expression and durable scheduling state.
- Webhook trigger configuration supports stable URL identity, encrypted signing secret, timestamp replay protection, delivery idempotency, and delivery audit linkage.
- Work items continue to store `IntegrationId` and `TriggerSource` so runtime agents remain trigger-agnostic.
- Existing scheduled, webhook, and manual behavior is migrated without losing execution history.
- API and UI expose trigger lists per integration instead of a single trigger type field.
- Tests cover one integration with both scheduled and webhook triggers, multiple schedules, duplicate webhook delivery IDs scoped to trigger/integration, disabled triggers, migration compatibility, and agent polling.

Design notes:

- Target shape: `integrations` stores code/run policy; `integration_triggers` stores trigger type, name, enabled state, and typed or JSON configuration.
- Schedule state should reference a trigger, not only an integration, so multiple schedules can coexist.
- Webhook delivery records reference the webhook trigger, not only the integration.
- Assembly scanning should sync integration metadata and trigger records from attributes after this foundation exists.

Completed notes:

- Added `integration_triggers` as the trigger configuration table.
- Removed trigger type, cron expression, and webhook secret from the integration model.
- Scheduled polling reads enabled scheduled triggers and stores schedule state by trigger.
- Webhook delivery resolves `/webhooks/{tenantSlug}/{integrationSlug}/{triggerSlug}` and stores work/delivery linkage by trigger.
- Capability tests added: one integration with both scheduled and webhook triggers; multiple scheduled triggers claimed independently with per-trigger schedule state; disabled scheduled trigger not claimed; per-trigger webhook routing within one integration; disabled webhook trigger returns 404; duplicate delivery IDs scoped per integration.
- EF model snapshot regenerated to match the new schedule-state-per-trigger index (`has-pending-model-changes` clean).
- Integration API create/update/list/get uses trigger arrays.
- UI sends and displays trigger records while keeping a simple single-trigger editor for now.
- Migration moves existing scheduled/webhook configuration into trigger rows.

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

- Webhook triggers get a generated shared secret and stable `/webhooks/{tenantSlug}/{integrationSlug}/{triggerSlug}` URL.
- Webhook delivery verifies `X-Integration-Signature` as an HMAC-SHA256 signature over `{X-Integration-Timestamp}.{raw request body}`.
- `X-Integration-Timestamp` enforces a 5-minute replay window.
- Optional `X-Integration-Delivery` provides idempotency and prevents duplicate work item creation per webhook trigger.
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
- Users can generate a personal API token for use with `serto deploy`.
- Tokens are securely hashed in the database.
- One-click "Copy Command" for `serto login` or `serto deploy`.

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

### `serto dev` Hot-Reload Loop

**Status:** Done

Create a habit-forming developer feedback loop.

Acceptance criteria:
- `serto dev` command watches local source files for changes.
- Automatically triggers a project build and `serto test` on file save.
- Clear, color-coded console output for success/failure.

Completed notes:
- Implemented `serto dev` command using `FileSystemWatcher`.
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

**Status:** Done

Automatically create or update integration records when a package is uploaded.

Prerequisite: Multi-Trigger Integration Model. Assembly scanning should create or update the executable integration and its trigger records separately, rather than writing a single `TriggerType` onto the integration.

Acceptance criteria:
- Control plane scans uploaded assemblies for types decorated with integration attributes.
- New integrations are auto-created in the database.
- Trigger attributes create or update trigger records for the discovered integration.
- Existing integrations and triggers are updated if their attributes have changed, such as a new cron expression.
- Typos in class names are eliminated by deriving them directly from the type.

Completed notes:

- Package upload scans assemblies for concrete `IIntegration` implementations decorated with SDK integration attributes.
- Scanning derives the fully qualified class name from the discovered type and pins auto-provisioned integrations to the uploaded package version.
- `[Integration]` provisions executable integration metadata without triggers.
- `[ScheduledIntegration]` and `[WebhookIntegration]` provision trigger records separately from the integration record, so a single discovered class can create multiple triggers.
- Existing integrations are upserted by slug; existing trigger records are updated by slug and webhook secrets are preserved unless a new webhook trigger is created.
- Discovered metadata is validated before the package row is created, preventing invalid attribute data such as bad cron expressions from creating unusable package versions.
- Scanner tests cover base integration metadata and multi-trigger discovery; upload tests cover package pinning, multi-trigger upsert, and invalid discovered cron handling.

### `serto` CLI

**Status:** Done

The primary entry point for developers.

Acceptance criteria:
- `serto init` scaffolds a new integration project.
- `serto dev` runs a local agent with hot-reload.
- `serto deploy` packages and uploads to the control plane.
- CLI is cross-platform (Windows, macOS, Linux).

---

## P1 — Runtime Reliability

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

### Orphaned Running Execution Reaper

**Status:** Done

Recover executions that are stuck in `Running` because the agent started them but never reported a terminal result — for example, the agent crashed mid-run, or lost its connection to the control plane during `FlushAsync`/`CompleteExecutionAsync` after a successful `StartExecutionAsync`.

This is a real wedge, not a theoretical one: the poll/claim path in `PollRepository` skips any integration that has an execution record in `Running` status (`runningIntegrations` guard), and nothing ages those records out. A single orphaned `Running` record therefore sidelines an integration permanently — its scheduled, manual, webhook, and retry work all stop dispatching — with no automatic recovery and no operator-facing signal. The only fix today is a manual SQL update to mark the record `Failed`.

This differs from work-item claims, which already self-heal: claims carry `ClaimExpiresAt` and are reclaimed after the 5-minute lease. Execution records have `StartedAt`/`CompletedAt` but no equivalent expiry, so the running guard never releases.

Acceptance criteria:

- Execution records stuck in `Running` past a configurable threshold move to a terminal `Failed` (or distinct `Orphaned`) state with a clear error message.
- Reaping releases the poll/claim running guard so the integration dispatches again.
- The threshold accounts for the integration's configured timeout, so a legitimately long run is not reaped early.
- Reaping is environment- and tenant-safe and does not race a late completion report for the same execution (a completion arriving after reap is handled consistently).
- Orphaned/reaped executions are visible in execution history and ideally surface as an operator signal.
- The reaper runs without an external scheduler (background service in the control plane) and is covered by tests for the stuck-Running case, the not-yet-expired case, and the late-completion-after-reap case.

Notes:

- Relates to [Manual Run Claim Failure Handling] (pre-start failures) — this item covers post-start, mid-execution loss. Together they close the gap of executions that never reach a terminal state.
- The triggering connection failure ("the response ended prematurely" on the agent → control-plane channel) is mitigated separately by agent HTTP connection-pool tuning; the reaper is the control-plane-side backstop regardless of cause.

Completed notes:

- Added `ReapOrphanedExecutionsHandler` + `IOrphanedExecutionRepository`: sweeps all tenants for `Running` execution records, marks those past their ceiling as `Failed` with an explanatory message, and mirrors the terminal status onto the linked work item so the poll/claim running guard releases.
- Per-record ceiling is the integration's configured timeout plus a grace window (`TimeoutGraceSeconds`, default 120s); integrations with no declared timeout fall back to `DefaultMaxRunningSeconds` (default 3600s). Both, plus the sweep interval, are configurable via the `OrphanedExecutionReaper` config section.
- Added `OrphanedExecutionReaper` background service. It invokes the handler directly inside a DI scope rather than through the auditing dispatcher, since a system sweep has no acting user; sweep failures are logged and retried rather than taking the service down.
- Reaping deliberately does not queue a retry — the cause is an unhealthy agent, so the next scheduled tick or a fresh manual run is the recovery path. A late completion report after reap is handled by the existing `CompleteExecutionHandler` (find-by-id then set status), so the real outcome still lands.
- Reaped executions remain in history as `Failed` with the orphaned reason, so they are visible in the UI.
- Tests cover stuck-past-default-ceiling, within-default-ceiling, within timeout+grace, past timeout+grace, work-item mirroring, no-retry-queued, and the empty-running case.

Remaining gaps:

- No distinct `Orphaned` execution status or dedicated operator alert yet — reaped runs surface as ordinary `Failed` with an explanatory message rather than a first-class signal.

---

## P1 — Logging And Observability

### Failed Execution Alerts

**Status:** Done

Notify operators when integrations fail. Shipped:

- Alerts configured at the **tenant** level (the default) with a **per-integration** override (`Inherit` / `Off` / `Custom`).
- **Email** and **outbound webhook** channels, each independently optional. Email goes through a platform-default ZeptoMail sender (operator-configured) or a tenant's own SMTP server; webhook posts JSON with an optional HMAC signature.
- Fires only on a **terminal** failure — failed/timed out with no retry remaining — so transient failures that retry stay quiet.
- Dispatched off the agent's request path by a background service; best-effort, per-channel failure isolation, logged. A **Send test alert** action verifies delivery at both levels. Config changes and test sends are audited; SMTP/webhook secrets are encrypted at rest.
- Outbound webhook URLs are SSRF-guarded: a connect-time IP check (defeating DNS rebinding) blocks private/loopback/link-local/metadata targets by default, opt-out via `AlertWebhooks:AllowPrivateNetworkTargets` for self-hosted internal endpoints.

Deferred (not blocking): alert scoping by environment, persisting individual alert-attempt records (currently logged), consecutive-failure thresholds, and per-attempt delivery retry.

---

## P1 — Integration Package Distribution

### Move Version Pin To Package Level

**Status:** Done

**Decided 2026-06-08, shipped 2026-06-09.** Integrations are now versioned at the **package** level,
not per-integration: activating a version moves every integration in the package to it together, so a
package's integrations can never split across versions.

**Implementation — Approach 1 (enforce in commands), chosen over the schema move.** Deploy already
repointed all of a package's integrations together (`UploadPackage` sets each provisioned
integration's `PackageId` to the new version), so divergence could only come from the *manual* repoint
path. We kept `Integration.PackageId` as the physical pin and made the only manual mutation
package-level — no schema change, hot dispatch path untouched.

What shipped:

- New `ActivatePackageVersionCommand` / `ActivatePackageVersionHandler` (`Features/IntegrationPackages`):
  repoints every integration on any version of the package name to the target version; skips (and
  reports) any whose class is absent from the target version.
- New endpoint `PUT /api/integration-packages/{id}/activate` (RequirePermission `ManageIntegrations`).
- Removed the per-integration `RepointIntegrationCommand` + `PUT /api/integrations/{id}/package`.
- Packages page: single one-click **Activate** per version (the per-integration picker is gone);
  the history page version selector now activates the whole package.
- `AuditAction.PackageActivated` added.

Trade-off accepted: no independent rollback of a single integration inside a shared package —
fix-forward with a new package version instead.

If structural enforcement is ever wanted (Approach 2 — `IsActive` flag per package name + dispatch
resolution change + migration), promote it then. Not needed unless divergence proves a real problem
in practice.

### Package Rollback

**Status:** Done

Allow reverting an integration to a previous package version.

Acceptance criteria:

- UI/API can select a previous package version.
- New executions use the selected version.
- Existing execution history remains tied to the original version.
- Rollback does not require database edits or manual filesystem changes.

Completed notes:

- Added a dedicated repoint endpoint `PUT /api/integrations/{id}/package` (`RepointIntegrationCommand`) that sets only the active package pin, audited. The target package is scanned and must contain the integration's class (not just exist), so a repoint — including a direct API call — can never pin to an incompatible package and leave the integration unable to load.
- Fixed a latent bug: `UpdateIntegrationHandler` set `PackageId` unconditionally, and the UI's update omits it, so every edit silently un-pinned the package. The general update no longer carries a package id at all, so it can never change or un-pin the package — that is exclusively the repoint endpoint's job.
- The integration history page shows the active version and (for `ManageIntegrations`) a picker over the other versions of the same package; selecting one repoints and takes effect on the next run (loaded as the isolated assembly for that version).
- Execution history is immutable across rollback because execution records snapshot package id/name/version as plain columns (no FK).
- A new Packages page lists packages grouped by name with per-version in-use/stale status and guarded deletion (see below).
- Tests cover repoint success/unknown-integration/unknown-package and the update-preserves-pin behavior.

Note: the per-integration repoint endpoint described above was subsequently superseded by package-level
activation (see "Move Version Pin To Package Level").

### Delete Stale Packages

**Status:** Done

Allow deleting package versions that are no longer in use, without breaking active integrations or history.

Completed notes:

- Added an in-use guard to `DeletePackageHandler`: a package that any integration is currently pinned to cannot be deleted (returns `409 Conflict` naming the integrations). This is required because `Integration.PackageId` is `OnDelete(SetNull)` — deleting an in-use package would otherwise silently un-pin the integration and break it at runtime.
- Execution history is unaffected by deletion (`ExecutionRecord.PackageId` has no FK; name/version are snapshotted), so past runs stay readable.
- The Packages page shows each version as **in use** (with the pinning integrations) or **stale**, and only allows deleting stale versions.
- Tests cover delete-when-unpinned and conflict-when-pinned.

Remaining gap:

- Deleting a package server-side does not remove an agent's already-extracted local copy (orphaned folder under `PackagesPath`); harmless but not cleaned up.

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

---

## P2 — Security And Administration

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

---

## P2 — Environment Management

### First-Class Environment Model

**Status:** Done

Replace free-form environment strings with tenant-managed environments.

Acceptance criteria:

- Tenants can create, edit, and delete environments. ✅ (Names are the canonical key and are immutable — "rename" is intentionally not supported; an explicit "disabled" state was not modeled and is deferred.)
- Integrations, secrets, tokens, and workflows reference known environments. ✅ (Validated on write against a per-tenant registry, backed by a composite foreign key.)
- Existing `production`, `staging`, and custom strings migrate cleanly. ✅ (Migration lowercases existing values, seeds the registry from distinct values, and ensures a default `production` per tenant.)
- UI has an environment selector. ✅ (Dedicated Environments admin page; Secrets, Integrations, and Agent tokens read environments from the shared registry instead of hardcoded lists.)

Implementation notes: environment names are canonicalized to lowercase via a single `EnvironmentKey.Normalize` helper. Live-config tables (secrets, integrations, agent tokens, workflows) carry a `(TenantId, Environment) → environments(TenantId, Name)` FK with `NoAction` (so tenant deletion still cascades while in-use environments can't be deleted); historical tables keep the string without an FK. The default environment cannot be deleted and its default flag cannot be cleared directly, so a valid default always exists; package auto-provisioning and the deploy secret check target that default environment. The runtime agent and the agent secrets endpoint both normalize the environment, so a mixed-case agent config still loads secrets. New permissions: `ViewEnvironments` / `ManageEnvironments`.

---

## P2 — Developer Experience

### SDK NuGet Package

**Status:** Done

Publish `Serto.Sdk` as a package.

Acceptance criteria:

- Package metadata is complete.
- Versioning strategy is documented.
- Example integration project consumes the package.
- Compatibility with runtime agent versions is documented.

Completed notes:

- Added NuGet metadata (version, authors, description, etc.) to `Sdk.csproj` and `Connectors.csproj`.

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

---

## Technical Debt

### Scheduled Claim Test Stability

**Status:** Done

Keep scheduled polling integration tests deterministic regardless of the current UTC time.

Completed notes:

- `PollRepositoryIntegrationTests.ClaimDueScheduledAsync_AcquiresLeaseAndPersistsScheduleState` now pins the integration `CreatedAt` before the test's fixed `now`.
- This prevents new `* * * * *` schedules from being evaluated as not due when the test runs after noon UTC.

### Secrets Page Environment Selector

**Status:** Done

Delivered as part of the [First-Class Environment Model](#first-class-environment-model). The Secrets
page now has an environment selector sourced from the shared registry; the secret list and all
mutations use the selected environment; and the Agent tokens and Integrations pages read their
environment choices from the same `useEnvironments` source, so the three are consistent. The previous
hardcoded `const ENVIRONMENT = 'production'` is gone.
