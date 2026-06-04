# API Reference

Base URL: `/api`

All endpoints that require authentication expect `Authorization: Bearer <jwt>` unless noted otherwise.

---

## Setup

### `GET /api/setup/status`

Returns whether initial setup has been completed.

**Auth:** None

**Response**
```json
{ "isComplete": true }
```

---

### `POST /api/setup`

One-time bootstrap. Creates the first tenant and admin user. Returns 409 if already complete.

**Auth:** None

**Request**
```json
{
  "tenantName": "Acme Corp",
  "tenantSlug": "acme-corp",
  "adminEmail": "admin@acme.com",
  "adminPassword": "securepassword"
}
```

**Response**
```json
{
  "tenantId": "uuid",
  "tenantName": "Acme Corp",
  "userId": "uuid",
  "email": "admin@acme.com",
  "token": "<jwt>"
}
```

---

## Tenants

### `POST /api/tenants/register` (Public)

Creates a new tenant and an initial admin user. Enables self-service SaaS onboarding.

**Auth:** None

**Request body:**

```json
{
  "tenantName": "Acme Corp",
  "tenantSlug": "acme",
  "adminEmail": "admin@acme.com",
  "adminPassword": "secure-password"
}
```

**Response (200 OK):**

```json
{
  "tenantId": "guid",
  "tenantName": "Acme Corp",
  "userId": "guid",
  "email": "admin@acme.com",
  "token": "jwt-token"
}
```

---

## Invitations

### `GET /api/invitations`

Lists pending invitations for the authenticated user's tenant. Accepted and expired invitations are not returned.

**Auth:** JWT (Admin)

**Response (200 OK):**

```json
{
  "invitations": [
    {
      "id": "guid",
      "email": "user@acme.com",
      "role": "Operator",
      "expiresAt": "iso-date",
      "acceptedAt": null
    }
  ]
}
```

### `POST /api/invitations`

Invites a new user to the current tenant.

**Auth:** JWT (Admin)

**Request body:**

```json
{
  "email": "user@acme.com",
  "role": "Member"
}
```

**Response (200 OK):**

```json
{
  "invitationId": "guid",
  "email": "user@acme.com",
  "token": "secure-token",
  "expiresAt": "iso-date"
}
```

### `POST /api/invitations/accept` (Public)

Accepts an invitation and creates a new user.

**Auth:** None

**Request body:**

```json
{
  "token": "secure-token",
  "password": "secure-password"
}
```

**Response (200 OK):**

```json
{
  "userId": "guid",
  "email": "user@acme.com",
  "token": "jwt-token"
}
```

---

## User Tokens (Personal Access Tokens)

Personal Access Tokens (PATs) allow secure access to the API from the `ip` CLI and other automated tools without requiring JWT/OAuth flows. Tokens are prefixed with `pat_` and are securely hashed in the database.

### `GET /api/user-tokens`

Lists all active tokens for the current user.

**Auth:** JWT

**Response (200 OK):**

```json
{
  "tokens": [
    {
      "id": "guid",
      "name": "My Laptop",
      "createdAt": "iso-date",
      "lastUsedAt": "iso-date or null"
    }
  ]
}
```

### `POST /api/user-tokens`

Generates a new PAT. **Note:** The plaintext token is only returned once.

**Auth:** JWT

**Request body:**

```json
{
  "name": "Production CLI"
}
```

**Response (201 Created):**

```json
{
  "id": "guid",
  "name": "Production CLI",
  "plaintextToken": "pat_...",
  "createdAt": "iso-date"
}
```

### `DELETE /api/user-tokens/{id}`

Revokes one of the current user's personal access tokens. Revocation is scoped to the authenticated user; one tenant user cannot revoke another user's personal access token.

**Auth:** JWT

**Response:** 204 No Content

---

## Auth

### `GET /api/auth/users`

Lists active users in the authenticated user's tenant.

**Auth:** JWT (Admin)

**Response**
```json
{
  "users": [
    {
      "id": "uuid",
      "email": "user@acme.com",
      "role": "Developer",
      "createdAt": "iso-date"
    }
  ]
}
```

### `POST /api/auth/register`

Legacy direct user registration within the authenticated user's tenant. This endpoint is admin-gated and creates a member user. Prefer invitations for assigning explicit `Developer`, `Operator`, or `Member` roles.

**Auth:** JWT (Admin)

**Request**
```json
{
  "email": "user@acme.com",
  "password": "securepassword"
}
```

**Response**
```json
{
  "userId": "uuid",
  "email": "user@acme.com"
}
```

---

### `POST /api/auth/login`

**Auth:** None

**Request**
```json
{
  "email": "admin@acme.com",
  "password": "securepassword"
}
```

**Response**
```json
{
  "token": "<jwt>",
  "email": "admin@acme.com",
  "role": "Admin"
}
```

---

## Integrations

### `GET /api/integrations`

**Auth:** JWT

**Query params:** `?environment=production` (optional)

**Response**
```json
{
  "integrations": [
    {
      "id": "uuid",
      "name": "Sync Orders",
      "slug": "sync-orders",
      "description": "Syncs orders from Shopify to the ERP",
      "environment": "production",
      "status": "Enabled",
      "triggerType": "Scheduled",
      "cronExpression": "0 * * * *",
      "className": "MyCompany.Integrations.SyncOrdersIntegration",
      "timeoutSeconds": 300,
      "retryMaxAttempts": 2,
      "retryBackoffSeconds": 60,
      "lastExecution": {
        "id": "uuid",
        "status": "Succeeded",
        "environment": "production",
        "startedAt": "2026-05-31T12:00:00Z",
        "completedAt": "2026-05-31T12:00:05Z",
        "durationMs": 5000,
        "errorMessage": null
      }
    }
  ]
}
```

---

### `POST /api/integrations`

**Auth:** JWT

**Request**
```json
{
  "name": "Sync Orders",
  "slug": "sync-orders",
  "description": "Optional description",
  "environment": "production",
  "triggerType": "Scheduled",
  "cronExpression": "0 * * * *",
  "className": "MyCompany.Integrations.SyncOrdersIntegration",
  "timeoutSeconds": 300,
  "retryMaxAttempts": 2,
  "retryBackoffSeconds": 60,
  "packageId": "uuid"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `name` | Yes | Display name |
| `slug` | Yes | URL-safe identifier (lowercase, hyphens only) |
| `environment` | Yes | Target environment (e.g. `production`, `staging`) |
| `triggerType` | Yes | `Scheduled`, `Webhook`, or `Manual` |
| `cronExpression` | When Scheduled | Cron expression for scheduling |
| `className` | Yes | Fully-qualified .NET type name that implements `IIntegration` |
| `timeoutSeconds` | No | Maximum execution duration in seconds. Must be greater than zero when provided. |
| `retryMaxAttempts` | No | Number of retry attempts after the initial attempt. Defaults to `0`. Must be non-negative. |
| `retryBackoffSeconds` | No | Delay before a retry work item becomes available. Defaults to immediate retry. Must be non-negative when provided. |
| `packageId` | No | Uploaded package version to execute. `null` keeps local agent path fallback. |

**Response:** `201 Created` with integration object. Webhook integrations also return `webhookUrl` and one-time `webhookSecret`.

---

### `GET /api/integrations/{id}`

**Auth:** JWT

**Response:** Integration object or `404`

---

### `POST /webhooks/{tenantSlug}/{integrationSlug}`

Receives an external webhook and queues a work item for the runtime agent.

**Auth:** HMAC signature

**Headers**

| Header | Required | Description |
|--------|----------|-------------|
| `X-Integration-Signature` | Yes | `sha256={hex_hmac}` where the HMAC is SHA-256 over the raw request body using the integration's webhook secret. |
| `X-Integration-Delivery` | No | Sender delivery ID for idempotency. Repeated IDs are acknowledged without creating another work item. |

**Response**

- `202 Accepted` when a new webhook work item is queued.
- `200 OK` when a duplicate delivery ID was already queued.
- `401 Unauthorized` for invalid signatures.
- `404 Not Found` for unknown, non-webhook, or disabled integrations.

---

### `GET /api/integrations/{id}/executions`

Returns recent executions for an integration, newest first.

**Auth:** JWT

**Query params:** `?limit=25` (optional, max 100)

**Response**
```json
{
  "executions": [
    {
      "id": "uuid",
      "status": "Succeeded",
      "environment": "production",
      "startedAt": "2026-05-31T12:00:00Z",
      "completedAt": "2026-05-31T12:00:05Z",
      "durationMs": 5000,
      "errorMessage": null,
      "attemptNumber": 1,
      "parentExecutionId": null,
      "rootExecutionId": null
    }
  ]
}
```

Retry executions use `attemptNumber` greater than `1`. `parentExecutionId` points to the immediately previous failed attempt, and `rootExecutionId` points to the original failed execution in the retry chain.

---

### `GET /api/integrations/{id}/executions/{executionId}/logs`

Returns structured logs recorded for a single execution, oldest first.

**Auth:** JWT

**Response**
```json
{
  "logs": [
    {
      "id": "uuid",
      "timestamp": "2026-05-31T12:00:01Z",
      "level": "Information",
      "message": "Processed 10 records",
      "exception": null,
      "propertiesJson": "{\"Count\":\"10\"}"
    }
  ]
}
```

---

### `PUT /api/integrations/{id}`

**Auth:** JWT

**Request**
```json
{
  "name": "Sync Orders",
  "description": "Updated description",
  "status": "Disabled",
  "cronExpression": "*/30 * * * *",
  "timeoutSeconds": 300
}
```

Set `timeoutSeconds` to `null` or omit it to run without a timeout. Timed-out executions are recorded with status `TimedOut`.

**Response:** Updated integration object

---

### `DELETE /api/integrations/{id}`

**Auth:** JWT

**Response:** `204 No Content` or `404`

---

### `POST /api/integrations/{id}/run`

Triggers a manual run of an integration. Creates a pending request that the next polling agent will claim and execute.

**Auth:** JWT

**Validation**
- Integration must exist and belong to the current tenant
- Integration must be enabled (cannot manually run a disabled integration)
- No existing pending manual run for this integration
- No currently running execution for this integration

**Response:** `202 Accepted`
```json
{
  "requestId": "uuid",
  "integrationId": "uuid",
  "integrationName": "Sync Orders",
  "environment": "production",
  "requestedAt": "2026-05-31T12:00:00Z"
}
```

**Errors**
- `404 Not Found` — integration does not exist
- `400 Bad Request` — integration is disabled
- `409 Conflict` — a manual run is already pending, or the integration is already running

---

## Secrets

### `GET /api/secrets/{environment}`

Returns secret keys and metadata. **Values are never returned.**

**Auth:** JWT

**Response**
```json
{
  "secrets": [
    {
      "id": "uuid",
      "key": "DATABASE_URL",
      "updatedAt": "2026-05-31T12:00:00Z"
    }
  ]
}
```

---

### `PUT /api/secrets/{environment}/{key}`

Create or update a secret. Idempotent — safe to call repeatedly.

**Auth:** JWT

Key must match `^[A-Z0-9_]+$`.

**Request**
```json
{ "value": "the secret value" }
```

**Response**
```json
{
  "id": "uuid",
  "key": "DATABASE_URL",
  "updatedAt": "2026-05-31T12:00:00Z"
}
```

---

### `DELETE /api/secrets/{environment}/{key}`

**Auth:** JWT

**Response:** `204 No Content` or `404`

---

## Integration packages

Integration packages are tenant-scoped zip archives containing compiled integration DLLs and their dependencies. The control plane stores package metadata and data, and runtime agents can sync packages through agent-token endpoints.

### `GET /api/integration-packages`

**Auth:** JWT

**Response**
```json
{
  "packages": [
    {
      "id": "uuid",
      "name": "MyCompany.Integrations",
      "version": "1.0.0",
      "fileName": "integrations.zip",
      "sizeBytes": 123456,
      "sha256Hash": "64-character lowercase hex hash",
      "createdAt": "2026-05-31T12:00:00Z"
    }
  ]
}
```

---

### `POST /api/integration-packages`

Uploads a new package version.

**Auth:** JWT

**Content-Type:** `multipart/form-data`

**Form fields**

| Field | Required | Description |
|-------|----------|-------------|
| `name` | Yes | Package name, e.g. `MyCompany.Integrations` |
| `version` | Yes | Version string, e.g. `1.0.0` |
| `file` | Yes | `.zip` archive containing at least one `.dll` |

**Validation**
- File must be a valid `.zip` archive
- File must contain at least one `.dll`
- File must be 100 MB or smaller
- `(name, version)` must be unique within the tenant

**Example**
```bash
curl -X POST http://localhost:5000/api/integration-packages \
  -H "Authorization: Bearer <jwt>" \
  -F "name=MyCompany.Integrations" \
  -F "version=1.0.0" \
  -F "file=@./publish/integrations.zip"
```

**Response:** `201 Created` with package metadata

---

### `GET /api/integration-packages/{id}`

**Auth:** JWT

**Response:** package metadata or `404`

---

### `GET /api/integration-packages/{id}/download`

Downloads the original zip archive.

**Auth:** JWT

**Response:** `application/zip` file or `404`

---

### `DELETE /api/integration-packages/{id}`

Deletes a stored package. This does not remove DLLs from any runtime agent.

**Auth:** JWT

**Response:** `204 No Content` or `404`

---

### `GET /api/agent/packages`

Lists package metadata available to the authenticated agent's tenant.

**Auth:** `X-Agent-Token`

**Response**
```json
{
  "packages": [
    {
      "id": "uuid",
      "name": "MyCompany.Integrations",
      "version": "1.0.0",
      "sha256Hash": "64-character lowercase hex hash"
    }
  ]
}
```

---

### `GET /api/agent/packages/{id}/download`

Downloads a package archive for the authenticated agent's tenant.

**Auth:** `X-Agent-Token`

**Response:** `application/zip` file or `404`

---

## Agent tokens

### `GET /api/agent-tokens`

**Auth:** JWT

**Response**
```json
{
  "tokens": [
    {
      "id": "uuid",
      "name": "Production agent",
      "environment": "production",
      "createdAt": "2026-05-31T12:00:00Z"
    }
  ]
}
```

---

### `GET /api/agent-tokens/heartbeats`

Lists latest heartbeat state for runtime agents in the tenant.

**Auth:** JWT

**Response**
```json
{
  "heartbeats": [
    {
      "id": "uuid",
      "agentTokenId": "uuid",
      "environment": "production",
      "version": "1.0.0.0",
      "hostname": "worker-01",
      "currentConcurrency": 1,
      "maxConcurrency": 5,
      "lastSeenAt": "2026-05-31T12:00:00Z",
      "isStale": false
    }
  ]
}
```

Agents are considered stale when the latest heartbeat is older than two minutes.

---

### `POST /api/agent-tokens`

**Auth:** JWT

**Request**
```json
{
  "name": "Production agent",
  "environment": "production"
}
```

**Response:** `201 Created`
```json
{
  "id": "uuid",
  "name": "Production agent",
  "environment": "production",
  "token": "agt_<base64url>",
  "createdAt": "2026-05-31T12:00:00Z"
}
```

> The `token` field is only present on creation. It cannot be retrieved again.

---

### `DELETE /api/agent-tokens/{id}`

**Auth:** JWT

**Response:** `204 No Content` or `404`

---

## Agent (runtime use only)

All agent endpoints use `X-Agent-Token: agt_<token>` header for authentication (not JWT).

### `GET /api/agent/integrations`

Claims and returns work items for the token's environment. Built-in work producers currently include due scheduled integrations, manual run requests, signed webhook deliveries, and retry attempts. Future trigger adapters should use the same work-item dispatch path.

**Auth:** `X-Agent-Token`

Calling this endpoint:
- Evaluates cron schedules for all enabled scheduled integrations in the token's environment
- Creates and claims due scheduled work items with a 5-minute claim lease
- Claims pending manual, webhook, and retry work items
- Updates `integration_schedule_states` with `last_dispatched_at` and `next_run_at`
- Skips integrations with active work items or running executions
- Can reclaim work items with expired claims

**Response**
```json
{
  "integrations": [
    {
      "id": "uuid",
      "name": "Sync Orders",
      "slug": "sync-orders",
      "triggerType": "Scheduled",
      "cronExpression": "0 * * * *",
      "className": "MyCompany.Integrations.SyncOrdersIntegration",
      "leaseExpiresAt": "2026-05-31T12:05:00Z",
      "triggerSource": "Scheduled",
      "manualRunRequestId": null,
      "workItemId": "uuid"
    },
    {
      "id": "uuid",
      "name": "Manual Job",
      "slug": "manual-job",
      "triggerType": "Manual",
      "cronExpression": null,
      "className": "MyCompany.Integrations.ManualJobIntegration",
      "leaseExpiresAt": "2026-05-31T12:05:00Z",
      "triggerSource": "Manual",
      "manualRunRequestId": "uuid",
      "workItemId": "uuid"
    },
    {
      "id": "uuid",
      "name": "Webhook Job",
      "slug": "webhook-job",
      "triggerType": "Webhook",
      "cronExpression": null,
      "className": "MyCompany.Integrations.WebhookJobIntegration",
      "leaseExpiresAt": "2026-05-31T12:05:00Z",
      "triggerSource": "Webhook",
      "manualRunRequestId": null,
      "workItemId": "uuid",
      "payload": "{\"event\":\"created\"}"
    }
  ]
}
```

| Field | Description |
|-------|-------------|
| `leaseExpiresAt` | When the work-item claim expires. If the agent crashes, another can reclaim after this time. |
| `triggerSource` | `Scheduled`, `Manual`, `Webhook`, or `Retry` — indicates how this run was triggered |
| `manualRunRequestId` | For manual runs, the ID of the originating manual run request. |
| `workItemId` | The claimed work item. Must be passed to `POST /api/agent/executions`. |
| `payload` | For webhook runs, the raw request body passed to `IIntegrationContext.Payload`. |

---

### `POST /api/agent/heartbeat`

Records runtime agent presence and capacity for the authenticated agent token.

**Auth:** `X-Agent-Token`

**Request**
```json
{
  "version": "1.0.0.0",
  "hostname": "worker-01",
  "currentConcurrency": 1,
  "maxConcurrency": 5
}
```

**Response:** `204 No Content`

---

### `GET /api/agent/secrets/{environment}`

Returns the full decrypted secret bundle for an environment. Called by the runtime agent before executing an integration.

**Auth:** `X-Agent-Token`

The token must be scoped to the requested environment — a token for `staging` cannot access `production`.

**Response**
```json
{
  "secrets": {
    "DATABASE_URL": "postgres://user:pass@host/db",
    "API_KEY": "sk_live_..."
  }
}
```

**Errors**
- `401 Unauthorized` — missing, invalid, or wrong-environment token

---

### `POST /api/agent/executions`

Opens an execution record before running an integration. The control plane validates:
- Work item exists and belongs to the token's tenant
- Work item claim is active and owned by this agent token
- Work item is in `Claimed` status
- Integration exists, is enabled, and matches the token's environment
- No other execution is currently running for the integration

**Auth:** `X-Agent-Token`

**Request**
```json
{
  "workItemId": "uuid"
}
```

| Field | Default | Description |
|-------|---------|-------------|
| `workItemId` | Required | The claimed work item to execute |

**Response:** `201 Created`
```json
{
  "executionId": "uuid",
  "startedAt": "2026-05-31T12:00:00Z"
}
```

**Errors**
- `401 Unauthorized` — invalid token
- `404 Not Found` — work item or integration does not exist
- `400 Bad Request` — claim expired, work item is not claimable, integration is disabled, or integration belongs to a different environment
- `409 Conflict` — work item is claimed by a different agent or the integration is already running

---

### `PUT /api/agent/executions/{id}`

Closes an execution record with the outcome.

**Auth:** `X-Agent-Token`

**Request**
```json
{
  "succeeded": true,
  "errorMessage": null,
  "isTimeout": false,
  "retryable": true
}
```

Or for failures:
```json
{
  "succeeded": false,
  "errorMessage": "Connection timeout after 30 seconds",
  "isTimeout": false,
  "retryable": true
}
```

When `retryable` is true and the integration still has retry attempts remaining, the control plane queues a retry work item. Agent shutdown cancellation should report `retryable: false`.

**Response:** `204 No Content`

---

### `POST /api/agent/executions/{id}/logs`

Records one structured log event for an execution. The token must belong to the same tenant and environment as the execution.

**Auth:** `X-Agent-Token`

**Request**
```json
{
  "timestamp": "2026-05-31T12:00:01Z",
  "level": "Information",
  "message": "Processed 10 records",
  "exception": null,
  "propertiesJson": "{\"Count\":\"10\"}"
}
```

**Response:** `204 No Content`

---

## Audit log

Audit entries record tenant-scoped security and configuration changes. Summaries are value-free: secret values, webhook secrets, and plaintext tokens are never returned.

### `GET /api/audit-log`

Lists recent audit entries for the authenticated user's tenant.

**Auth:** JWT or PAT with `Admin` role

**Query parameters:**

| Name | Type | Default | Notes |
|------|------|---------|-------|
| `limit` | integer | `50` | Clamped to `1..200` |

**Response:**

```json
{
  "entries": [
    {
      "id": "uuid",
      "actorUserId": "uuid",
      "actorEmail": "admin@example.com",
      "action": "SecretSet",
      "targetType": "Secret",
      "targetId": "production/API_KEY",
      "summary": "Set secret 'API_KEY' in production",
      "occurredAt": "2026-06-03T12:00:00Z"
    }
  ]
}
```

Actions include secret set/delete, integration create/update/delete, agent token create/revoke, personal access token create/revoke, package upload/delete, user invite, and invitation accept.

---

## Error format

All errors follow the RFC 9457 Problem Details format:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "Tenant name is required."
}
```

| HTTP status | Condition |
|-------------|-----------|
| 400 | Validation error |
| 401 | Missing or invalid auth |
| 404 | Resource not found |
| 409 | Conflict (e.g. duplicate slug, setup already complete) |
| 500 | Unexpected server error |
