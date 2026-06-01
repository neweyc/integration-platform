# Backlog

This backlog tracks implementation work that is not yet complete. It is ordered by practical product value and production risk, not by long-term vision. The roadmap remains the phase-level view; this file is the actionable work queue.

Status key:

- `Todo` — not started
- `In Progress` — partially implemented or actively being built
- `Blocked` — needs a decision or prerequisite
- `Done` — completed and verified

---

## P0 — MVP Completion

### Durable Scheduling State

**Status:** Todo

Store scheduling state in the control plane instead of the agent's in-memory `_lastRun` dictionary.

Acceptance criteria:

- Each scheduled integration has persisted last-dispatched or last-completed scheduling state.
- Agent restart does not cause every cron integration to re-evaluate from `DateTime.MinValue`.
- Due calculation is deterministic across restarts.
- Execution start is guarded so the same due run cannot be started twice by accident.
- Tests cover restart behavior and duplicate-dispatch prevention.

Notes:

- This is required before multi-agent or production scheduling is reliable.
- Likely needs a `next_run_at`, `last_run_at`, or scheduling lease model.

### Graceful Agent Shutdown

**Status:** Todo

Make the runtime agent drain or cancel running work predictably during shutdown.

Acceptance criteria:

- Agent stops polling when cancellation is requested.
- In-flight executions either finish within a configured drain window or are cancelled.
- Cancelled executions are reported to the control plane with a useful failure message.
- Buffered execution logs are flushed before process exit.
- Tests cover shutdown during active execution.

### Manual Run Support

**Status:** Todo

Add a user-facing way to trigger an integration immediately.

Acceptance criteria:

- API endpoint exists to request a manual run for an integration.
- UI exposes a `Run now` action.
- Manual runs create normal execution records and logs.
- Disabled integrations cannot be manually run unless explicitly allowed by design.
- Scheduled and manual executions do not overlap for the same integration.

---

## P1 — Runtime Reliability

### Retry Policy

**Status:** Todo

Add configurable retry behavior for failed integrations.

Acceptance criteria:

- Integration has retry count and backoff settings.
- Failed executions can create retry attempts.
- Execution history shows original attempt and retries clearly.
- Retry exhaustion produces a final failed state.
- Tests cover retry success, retry exhaustion, and non-retryable cancellation.

### Execution Timeouts

**Status:** Todo

Prevent integrations from running forever.

Acceptance criteria:

- Integration execution has a configurable timeout.
- Timeout cancels the integration through `CancellationToken`.
- Timeout is recorded distinctly in execution history.
- Logs include timeout context.

### Agent Heartbeats

**Status:** Todo

Track runtime agent presence and health in the control plane.

Acceptance criteria:

- Agent periodically reports heartbeat, environment, version, hostname, and current concurrency.
- Control plane stores last-seen timestamp.
- UI shows active/stale agents.
- Agent token revocation is reflected in heartbeat failure behavior.

### Agent Version Reporting

**Status:** Todo

Report agent version and SDK compatibility to the control plane.

Acceptance criteria:

- Runtime agent reports version during heartbeat or poll.
- Control plane records version per agent.
- UI shows version and stale/unsupported state.
- Compatibility checks can warn when packages require a newer agent.

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

**Status:** Todo

Connect control-plane package storage to runtime agents.

Acceptance criteria:

- Agent can list or fetch packages authorized for its tenant/environment.
- Package download verifies SHA-256 before activation.
- Package is extracted into an agent-managed directory.
- Agent can detect new package versions without manual copy.
- Package activation does not corrupt currently running executions.

### Integration Version Pinning

**Status:** Todo

Tie integration definitions to a package/version.

Acceptance criteria:

- Integration record can reference a package version.
- Agent resolves class name from the pinned package.
- Execution record stores package id/version used.
- UI shows package/version on integration detail and execution history.

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

**Status:** Todo

Record security- and configuration-relevant actions.

Acceptance criteria:

- Secret create/update/delete events are audited without secret values.
- Integration create/update/delete events are audited.
- Agent token create/revoke events are audited.
- Package upload/delete events are audited.
- Audit entries include actor, tenant, timestamp, action, and target id.

### Role Enforcement

**Status:** Todo

Make user roles meaningful.

Acceptance criteria:

- Admin-only operations are enforced server-side.
- Member role has read-only access where appropriate.
- UI hides unavailable actions for non-admin users.
- Tests cover authorization failures.

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

**Status:** Todo

Publish `IntegrationPlatform.Sdk` as a package.

Acceptance criteria:

- Package metadata is complete.
- Versioning strategy is documented.
- Example integration project consumes the package.
- Compatibility with runtime agent versions is documented.

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

**Status:** Todo

Allow public sign-up for SaaS mode.

Acceptance criteria:

- New tenant and first admin can self-register.
- Duplicate tenant slugs are handled.
- Email verification is supported or explicitly deferred.

### User Invitations

**Status:** Todo

Allow tenant admins to invite users.

Acceptance criteria:

- Admin can invite by email.
- Invite token expires.
- Invite acceptance creates user in correct tenant.
- Invite events are audited.

### Password Reset

**Status:** Todo

Support forgotten-password flow.

Acceptance criteria:

- Reset request does not reveal whether an email exists.
- Reset token expires.
- Password change invalidates old sessions where applicable.

### Usage Metering

**Status:** Todo

Track billable usage.

Acceptance criteria:

- Executions are counted per tenant and billing period.
- Package storage usage can be measured.
- Usage API supports billing dashboard.

### Billing Integration

**Status:** Todo

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
