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

Each installation has exactly one tenant (single-tenant deployment). Multi-tenant support is built into the data model — every resource carries a `TenantId` — but the UI and setup flow currently assume one tenant per deployment.

This is intentional: self-hosted customers get a clean single-tenant experience. A future SaaS deployment could onboard multiple tenants to a shared control plane.

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
| Trigger type | `Scheduled`, `Webhook`, or `Manual` |
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

Secrets are namespaced by environment string. There is no fixed list of environments — any string is valid (`production`, `staging`, `dev`, `uat`, etc.). An agent token is scoped to a single environment, so a production agent cannot access staging secrets.

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
10. (future) Alerts triggered if execution fails
```

Scheduling state is stored in `integration_schedule_states` with `last_dispatched_at` and `next_run_at`. New scheduled integrations calculate their first run from the integration creation time. Existing scheduled integrations use the persisted `next_run_at`, so agent restarts do not cause all jobs to run immediately.

Current limitation: schedule claiming advances state before the agent starts execution. If an agent crashes after claiming work but before opening an execution record, that scheduled occurrence may be skipped. A future lease model should allow abandoned claims to expire and be retried.

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

Current operational limitation: uploaded packages are not automatically distributed to runtime agents. Operators still need to copy or extract DLLs and dependencies into the agent's `IntegrationsPath` and restart the agent. Package storage is a foundation for future agent sync, version pinning, and rollback.

---

## User roles

| Role | Capabilities |
|------|-------------|
| Admin | Full access — manage integrations, secrets, tokens, users |
| Member | (future) Read access, can view integrations and execution history |

Currently all authenticated users within a tenant have admin-level access. Role-based restrictions will be enforced when the Member role is surfaced in the UI.

---

## Data retention

No automatic data retention policies are implemented yet. Execution history and logs need a retention policy to prevent unbounded database growth. Recommended approach: keep 90 days of execution records and execution logs, configurable per tenant.
