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

### `POST /api/invitations/{id}/resend`

Rotates the pending invitation token and extends the invitation expiry. The old accept link stops working.

**Auth:** JWT (Admin)

**Response (200 OK):**

```json
{
  "invitationId": "guid",
  "email": "user@acme.com",
  "role": "Member",
  "token": "new-secure-token",
  "expiresAt": "iso-date"
}
```

Returns `404 Not Found` when the invitation does not exist in the tenant, has expired, or has already been accepted.

### `DELETE /api/invitations/{id}`

Revokes a pending invitation by expiring it immediately. Revoked invitations are removed from the pending list and cannot be accepted.

**Auth:** JWT (Admin)

**Response:** `204 No Content`

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

Personal Access Tokens (PATs) allow secure access to the API from the `serto` CLI and other automated tools without requiring JWT/OAuth flows. Tokens are prefixed with `pat_` and are securely hashed in the database.

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
      "className": "MyCompany.Integrations.SyncOrdersIntegration",
      "triggers": [
        {
          "id": "uuid",
          "name": "Schedule",
          "slug": "schedule",
          "type": "Scheduled",
          "enabled": true,
          "cronExpression": "0 * * * *"
        }
      ],
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
  "className": "MyCompany.Integrations.SyncOrdersIntegration",
  "triggers": [
    {
      "name": "Schedule",
      "slug": "schedule",
      "type": "Scheduled",
      "enabled": true,
      "cronExpression": "0 * * * *"
    }
  ],
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
| `className` | Yes | Fully-qualified .NET type name that implements `IIntegration` |
| `triggers` | Yes | Trigger records. Empty array means manual/operator-triggered only. |
| `timeoutSeconds` | No | Maximum execution duration in seconds. Must be greater than zero when provided. |
| `retryMaxAttempts` | No | Number of retry attempts after the initial attempt. Defaults to `0`. Must be non-negative. |
| `retryBackoffSeconds` | No | Delay before a retry work item becomes available. Defaults to immediate retry. Must be non-negative when provided. |
| `packageId` | No | Uploaded package version to execute. `null` keeps local agent path fallback. |

**Response:** `201 Created` with integration object. Webhook trigger records also return `webhookUrl` and one-time `webhookSecret`.

---

### `GET /api/integrations/{id}`

**Auth:** JWT

**Response:** Integration object or `404`

---

### `GET /api/trigger-adapters`

Lists trigger adapter descriptors available to the control plane. This is a discovery endpoint for UI/tooling; queue and file adapters are descriptors until concrete listeners are implemented.

**Auth:** JWT with `ViewIntegrations`

**Response**
```json
{
  "adapters": [
    {
      "key": "scheduled",
      "displayName": "Scheduled",
      "source": "Scheduled",
      "triggerType": "Scheduled",
      "requiresStoredTrigger": true,
      "supportsPayload": false,
      "supportsDeduplication": false,
      "description": "Evaluates cron state for enabled scheduled triggers and creates due work items."
    },
    {
      "key": "queue",
      "displayName": "Queue",
      "source": "Queue",
      "triggerType": "Queue",
      "requiresStoredTrigger": true,
      "supportsPayload": true,
      "supportsDeduplication": true,
      "description": "Future adapter for queue and event-bus messages such as SQS, Azure Service Bus, RabbitMQ, and Kafka."
    }
  ]
}
```

---

### `GET /api/trigger-events`

Lists persisted trigger adapter events for operator observability. Events record when an adapter receives, accepts, rejects, deduplicates, or converts a trigger event into work. Built-in scheduled polling, workflow dispatch, and retry scheduling also record `ConvertedToWork` events.

**Auth:** JWT with `ViewExecutions`

**Query params**

| Param | Required | Description |
|-------|----------|-------------|
| `integrationId` | No | Filter to one integration |
| `triggerId` | No | Filter to one stored trigger |
| `adapterKey` | No | Filter by adapter, such as `webhook`, `manual`, `queue`, or `file` |
| `outcome` | No | `Received`, `Accepted`, `Deduplicated`, `Rejected`, `ConvertedToWork`, or `Failed` |
| `limit` | No | Number of rows to return, 1-200. Defaults to 50. |

**Response**
```json
{
  "events": [
    {
      "id": "uuid",
      "integrationId": "uuid",
      "integrationTriggerId": "uuid",
      "adapterKey": "webhook",
      "source": "Webhook",
      "eventKey": "delivery-123",
      "outcome": "ConvertedToWork",
      "workItemId": "uuid",
      "metadataJson": null,
      "errorMessage": null,
      "receivedAt": "2026-06-04T12:00:00Z"
    }
  ]
}
```

---

### `POST /webhooks/{tenantSlug}/{integrationSlug}/{triggerSlug}`

Receives an external webhook and queues a work item for the runtime agent.

**Auth:** HMAC signature

**Headers**

| Header | Required | Description |
|--------|----------|-------------|
| `X-Integration-Signature` | Yes | `sha256={hex_hmac}` where the HMAC is SHA-256 over `{X-Integration-Timestamp}.{raw request body}` using the webhook trigger's secret. |
| `X-Integration-Timestamp` | Yes | Unix timestamp in seconds. Requests outside the 5-minute tolerance window are rejected. |
| `X-Integration-Delivery` | No | Sender delivery ID for idempotency. Repeated IDs for the same webhook trigger are acknowledged without creating another work item. |

**Response**

- `202 Accepted` when a new webhook work item is queued.
- `200 OK` when a duplicate delivery ID was already queued.
- `401 Unauthorized` for invalid signatures or stale/missing timestamps.
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
  "triggers": [
    {
      "name": "Schedule",
      "slug": "schedule",
      "type": "Scheduled",
      "enabled": false,
      "cronExpression": "*/30 * * * *"
    }
  ],
  "timeoutSeconds": 300
}
```

Set `timeoutSeconds` to `null` or omit it to run without a timeout. Timed-out executions are recorded with status `TimedOut`.

The general update does not accept a package id — the active version is a package-level property and is changed through the activate endpoint (see `PUT /api/integration-packages/{id}/activate`), so an edit never alters or un-pins the package.

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

## Environments

The per-tenant registry of deployment environments. Environment names are canonicalized to lowercase
(`^[a-z0-9-]+$`). Every environment-scoped write (secrets, integrations, agent tokens, workflows)
validates its environment against this registry, so an unknown environment is rejected rather than
silently created.

### `GET /api/environments`

Lists the tenant's environments, ordered by sort order then name.

**Auth:** JWT — `ViewEnvironments`

**Response**
```json
{
  "environments": [
    { "name": "production", "displayName": "Production", "description": null, "sortOrder": 0, "isDefault": true }
  ]
}
```

---

### `POST /api/environments`

Creates an environment. The name is canonicalized to lowercase. Returns `409` if it already exists.

**Auth:** JWT — `ManageEnvironments`

**Request**
```json
{ "name": "staging", "displayName": "Staging", "description": null, "sortOrder": 1, "isDefault": false }
```

**Response:** the created environment (same shape as the list items).

---

### `PUT /api/environments/{name}`

Updates an environment's display name, description, sort order, and default flag. The name itself is
immutable (it is the key other records reference).

**Auth:** JWT — `ManageEnvironments`

---

### `DELETE /api/environments/{name}`

Deletes an environment. Returns `409` if it is the default environment (make another the default
first), or if any integration, secret, agent token, or workflow still references it (with a message
listing what to move or remove first).

**Auth:** JWT — `ManageEnvironments`

**Response:** `204 No Content`, `404`, or `409`

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

Key must match `^[A-Z0-9_]+$`. The `{environment}` must exist in the [environment registry](#environments); an unknown environment returns `400`.

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
| `requiredSecrets` | No | Comma-separated secret names the package needs (e.g. `ERP_API_KEY,DB_CONNECTION_STRING`). Detected by the client-side source scan (`serto scan`/`deploy`) and used only to build the `secretCheck` in the response. Omit it to skip the check. |

**Validation**
- File must be a valid `.zip` archive
- File must contain at least one `.dll`
- File must be 100 MB or smaller
- `(name, version)` must be unique within the tenant
- Discovered SDK integration attributes must produce valid integration and trigger metadata

**Example**
```bash
curl -X POST http://localhost:5000/api/integration-packages \
  -H "Authorization: Bearer <jwt>" \
  -F "name=MyCompany.Integrations" \
  -F "version=1.0.0" \
  -F "file=@./publish/integrations.zip" \
  -F "requiredSecrets=ERP_API_KEY,DB_CONNECTION_STRING"
```

**Response:** `201 Created` with package metadata, a provisioning report, and a secret check. Upload also scans decorated `IIntegration` classes and auto-provisions matching integration and trigger records pinned to the uploaded package version.

```json
{
  "package": {
    "id": "0aa96bc3-6c90-4a99-8ac6-b5c742e559c9",
    "name": "MyCompany.Integrations",
    "version": "1.0.0",
    "fileName": "integrations.zip",
    "sizeBytes": 18432,
    "sha256Hash": "e3b0c44298fc1c149afbf4c8996fb924...",
    "createdAt": "2026-06-05T19:30:00Z"
  },
  "provisioning": [
    {
      "id": "5073ddcc-0dd9-41e3-b5b0-b31608369c56",
      "name": "Order Sync",
      "slug": "order-sync",
      "environment": "production",
      "className": "Acme.OrderSync",
      "action": "Updated",
      "packageId": "0aa96bc3-6c90-4a99-8ac6-b5c742e559c9",
      "triggers": [
        {
          "id": "d4eb68fa-23f5-4748-9927-5df0508607e9",
          "name": "Every Five",
          "slug": "every-five",
          "type": "Scheduled",
          "enabled": true,
          "action": "Created",
          "cronExpression": "*/5 * * * *",
          "nextRunAt": "2026-06-05T19:35:00Z",
          "declaredCronExpression": "0 * * * *",
          "cronOverridden": true,
          "enabledOverridden": false
        },
        {
          "id": "ed54eea2-3690-4a9a-bd23-4804518cc1ff",
          "name": "Hook",
          "slug": "hook",
          "type": "Webhook",
          "enabled": true,
          "action": "Updated",
          "webhookUrl": "/webhooks/acme/order-sync/hook",
          "webhookSecretPreserved": true
        }
      ]
    }
  ],
  "secretCheck": {
    "environment": "production",
    "required": ["DB_CONNECTION_STRING", "ERP_API_KEY"],
    "satisfied": ["ERP_API_KEY"],
    "missing": ["DB_CONNECTION_STRING"]
  }
}
```

Webhook secrets are preserved when an existing webhook trigger is updated by package upload; upload does not rotate operator-facing webhook credentials.

A package upload records the code-declared cron/enabled as each trigger's defaults but **preserves operator overrides**: if an operator disabled a trigger or set a production cron different from the code default, a redeploy keeps the operator's value and reports the divergence as drift. On each provisioned trigger, `declaredCronExpression` is the cron the code last declared, `cronOverridden` / `enabledOverridden` indicate an active operator override (active value differs from the declared default), and `cronExpression` / `enabled` are the active runtime values. The same `declaredCronExpression` / `cronOverridden` / `enabledOverridden` fields appear on triggers returned by `GET /api/integrations` and `GET /api/integrations/{id}`.

The `secretCheck` object compares the `requiredSecrets` form field against the secrets configured in the provisioning environment (the tenant's [default environment](#environments)):

| Field | Description |
|-------|-------------|
| `environment` | Environment whose configured secrets were checked — the tenant's default environment, which is also where discovered integrations are auto-provisioned. |
| `required` | Distinct required secret names received, sorted, case-insensitively de-duplicated. |
| `satisfied` | Required names that are configured in the environment. |
| `missing` | Required names that are **not** configured. Integrations needing these will fail at runtime until they are set. |

The check is advisory: a non-empty `missing` list does **not** fail the upload. Matching is case-insensitive. When `requiredSecrets` is omitted, all three arrays are empty.

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

### `PUT /api/integration-packages/{id}/activate`

Activates this package version for its **whole package**. Integrations are versioned at the package level: every integration currently running any version of the same package name is repointed to this version together, so a package's integrations can never split across versions (rollback / roll-forward applies to all of them at once). Takes effect on the next run; the agent loads the selected version's isolated assembly. Execution history is unaffected — past records keep the version they ran.

An integration whose class is **absent** from the target version (renamed/removed) is left on its current version and reported in `skipped` rather than being broken.

**Auth:** JWT (`ManageIntegrations`) — it mutates integration bindings, not just package storage.

**Response:** `200 OK`; `404` if the package is not found.
```json
{
  "packageName": "MyCompany.Integrations",
  "version": "2026.06.09.142210",
  "activated": ["Sync Orders", "Export Invoices"],
  "skipped": []
}
```

---

### `DELETE /api/integration-packages/{id}`

Deletes a stored package. This does not remove DLLs from any runtime agent.

A package that is the active version of one or more integrations cannot be deleted — activate another version first (which moves those integrations off it). Deleting it would otherwise silently un-pin them. Execution history is unaffected by deletion (records snapshot the package name/version).

**Auth:** JWT (`ManagePackages`)

**Response:** `204 No Content`, `404` if not found, or `409 Conflict` if the package is in use (the message names the integrations pinned to it).

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

## Workflows

Workflow definitions are DAGs whose nodes reference existing integrations. Runtime agents execute workflow nodes through the same work-item claim/start/complete path used by scheduled, manual, webhook, and retry work.

### `GET /api/workflows`

Lists workflow definitions for the authenticated tenant.

**Auth:** JWT or PAT with `Developer`, `Operator`, `Member`, or `Admin` role

**Query parameters:**

| Name | Type | Default | Notes |
|------|------|---------|-------|
| `environment` | string | none | Optional environment filter |

**Response:** `200 OK`

### `POST /api/workflows`

Creates a workflow definition.

**Auth:** JWT or PAT with `Developer` or `Admin` role

**Request**
```json
{
  "name": "Order workflow",
  "slug": "order-workflow",
  "environment": "production",
  "nodes": [
    { "key": "extract", "name": "Extract orders", "integrationId": "uuid" },
    { "key": "load", "name": "Load orders", "integrationId": "uuid" }
  ],
  "edges": [
    { "from": "extract", "to": "load" }
  ]
}
```

**Response:** `201 Created`

### `POST /api/workflows/{id}/run`

Starts a workflow run and queues root nodes.

**Auth:** JWT or PAT with `Developer`, `Operator`, or `Admin` role

**Response:** `202 Accepted`

```json
{
  "id": "uuid",
  "workflowDefinitionId": "uuid",
  "status": "Running",
  "startedAt": "2026-06-04T12:00:00Z",
  "completedAt": null,
  "nodes": [
    {
      "id": "uuid",
      "workflowNodeId": "uuid",
      "nodeKey": "extract",
      "nodeName": "Extract orders",
      "integrationId": "uuid",
      "status": "Queued",
      "workItemId": "uuid",
      "executionRecordId": null
    }
  ]
}
```

### `GET /api/workflows/{id}/runs`

Lists recent workflow runs and node states.

**Auth:** JWT or PAT with `Developer`, `Operator`, `Member`, or `Admin` role

**Query parameters:**

| Name | Type | Default | Notes |
|------|------|---------|-------|
| `limit` | integer | `25` | Clamped to `1..100` |

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
| `triggerSource` | `Scheduled`, `Manual`, `Webhook`, `Retry`, `Workflow`, `Queue`, or `File` — indicates how this run was triggered |
| `manualRunRequestId` | For manual runs, the ID of the originating manual run request. |
| `workItemId` | The claimed work item. Must be passed to `POST /api/agent/executions`. |
| `payload` | Normalized trigger payload passed to `IIntegrationContext.Payload` for payload-capable triggers such as webhook, queue, and file. |

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

## Failure alerts

Notify recipients when an integration fails and no retry remains (a *terminal* failure). Two channels, each independently optional: **email** and an **outbound webhook**. Settings exist at the tenant level (the default for every integration) and can be overridden per integration.

**Email provider.** Email is sent through the platform-default mailer (ZeptoMail, configured by the operator — see [installation](installation.md#configuration-reference)) unless a tenant configures its own SMTP server, in which case email is sent from there. The SMTP server is always tenant-level; per-integration overrides only change recipients and the webhook destination.

Secrets (the SMTP password and the webhook signing secret) are encrypted at rest and never returned. In request bodies, secret fields follow the convention **omit = leave unchanged, empty string = clear, value = set**.

Webhook URLs are restricted to public `http(s)` endpoints by default: targets resolving to private, loopback, link-local, or cloud-metadata addresses are rejected (SSRF protection), both at save time and at connect time. Operators can lift this for internal endpoints via `AlertWebhooks:AllowPrivateNetworkTargets` (see [installation](installation.md#configuration-reference)).

Permissions: `ViewAlerts` to read, `ManageAlerts` to change or test.

### `GET /api/alerts/settings`

Returns the tenant-default alert settings. Secrets are returned as `…Set` booleans only.

**Auth:** JWT — requires `ViewAlerts`

**Response**
```json
{
  "emailEnabled": true,
  "emailRecipients": "ops@acme.com, oncall@acme.com",
  "smtpHost": null,
  "smtpPort": 587,
  "smtpUseStartTls": true,
  "smtpUsername": null,
  "smtpPasswordSet": false,
  "smtpFromAddress": null,
  "smtpFromName": null,
  "webhookEnabled": true,
  "webhookUrl": "https://hooks.slack.com/services/…",
  "webhookSecretSet": false,
  "zeptoConfigured": true,
  "zeptoFromAddress": "alerts@serto.io"
}
```

---

### `PUT /api/alerts/settings`

Create or update the tenant-default alert settings.

**Auth:** JWT — requires `ManageAlerts`

**Request body** — `smtpPassword` and `webhookSecret` follow the omit/empty/value convention above.
```json
{
  "emailEnabled": true,
  "emailRecipients": "ops@acme.com, oncall@acme.com",
  "smtpHost": "smtp.acme.com",
  "smtpPort": 587,
  "smtpUseStartTls": true,
  "smtpUsername": "mailer",
  "smtpPassword": "•••",
  "smtpFromAddress": "alerts@acme.com",
  "smtpFromName": "Acme Alerts",
  "webhookEnabled": true,
  "webhookUrl": "https://hooks.slack.com/services/…",
  "webhookSecret": "•••"
}
```

**Response:** the updated settings (same shape as `GET`).

---

### `POST /api/alerts/settings/test`

Send a sample alert through the current tenant-default configuration so you can confirm delivery (especially SMTP).

**Auth:** JWT — requires `ManageAlerts`

**Response**
```json
{
  "emailAttempted": true,
  "emailSucceeded": true,
  "emailError": null,
  "webhookAttempted": true,
  "webhookSucceeded": false,
  "webhookError": "Response status code does not indicate success: 404 (Not Found)."
}
```

Returns `400` if no channel is configured to send to.

---

### `GET /api/alerts/integrations/{integrationId}/settings`

Returns the per-integration override.

**Auth:** JWT — requires `ViewAlerts`

**Response**
```json
{
  "integrationId": "uuid",
  "mode": "Inherit",
  "emailEnabled": false,
  "emailRecipients": null,
  "webhookEnabled": false,
  "webhookUrl": null,
  "webhookSecretSet": false
}
```

`mode` is one of `Inherit` (use tenant defaults), `Off` (suppress alerts for this integration), or `Custom` (use the destinations below). When no override has been saved, the effective mode is `Inherit`.

---

### `PUT /api/alerts/integrations/{integrationId}/settings`

Create or update the per-integration override. Email (when `Custom`) still relays through the tenant's email sender; only recipients and the webhook destination are integration-specific.

**Auth:** JWT — requires `ManageAlerts`

**Request body**
```json
{
  "mode": "Custom",
  "emailEnabled": true,
  "emailRecipients": "team@acme.com",
  "webhookEnabled": false,
  "webhookUrl": null,
  "webhookSecret": null
}
```

**Response:** the updated override (same shape as `GET`).

---

### `POST /api/alerts/integrations/{integrationId}/settings/test`

Send a sample alert through this integration's effective configuration (honoring its override).

**Auth:** JWT — requires `ManageAlerts`

**Response:** same shape as the tenant test above.

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
