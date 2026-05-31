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

## Auth

### `POST /api/auth/register`

Register a new user within the current tenant. First user automatically becomes Admin.

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
  "email": "user@acme.com",
  "role": "Member",
  "token": "<jwt>"
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
      "className": "MyCompany.Integrations.SyncOrdersIntegration"
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
  "className": "MyCompany.Integrations.SyncOrdersIntegration"
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

**Response:** `201 Created` with integration object

---

### `GET /api/integrations/{id}`

**Auth:** JWT

**Response:** Integration object or `404`

---

### `PUT /api/integrations/{id}`

**Auth:** JWT

**Request**
```json
{
  "name": "Sync Orders",
  "description": "Updated description",
  "status": "Disabled",
  "cronExpression": "*/30 * * * *"
}
```

**Response:** Updated integration object

---

### `DELETE /api/integrations/{id}`

**Auth:** JWT

**Response:** `204 No Content` or `404`

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

Returns all enabled integrations for the token's environment. The runtime agent polls this endpoint to determine which integrations are due for execution.

**Auth:** `X-Agent-Token`

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
      "className": "MyCompany.Integrations.SyncOrdersIntegration"
    }
  ]
}
```

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

Opens an execution record before running an integration. The control plane validates that the integration exists, belongs to the token's tenant/environment, and is enabled.

**Auth:** `X-Agent-Token`

**Request**
```json
{
  "integrationId": "uuid"
}
```

**Response:** `201 Created`
```json
{
  "executionId": "uuid",
  "startedAt": "2026-05-31T12:00:00Z"
}
```

**Errors**
- `401 Unauthorized` — invalid token
- `404 Not Found` — integration does not exist or belongs to different tenant
- `400 Bad Request` — integration is disabled or belongs to different environment

---

### `PUT /api/agent/executions/{id}`

Closes an execution record with the outcome.

**Auth:** `X-Agent-Token`

**Request**
```json
{
  "succeeded": true,
  "errorMessage": null
}
```

Or for failures:
```json
{
  "succeeded": false,
  "errorMessage": "Connection timeout after 30 seconds"
}
```

**Response:** `204 No Content`

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
