# Architecture

## Overview

Integration Platform is a code-first integration platform. Users write integrations as C# classes rather than configuring them in a low-code editor. The platform handles scheduling, secrets injection, execution, and observability.

The system is split into two independently deployable components:

```
┌─────────────────────────────────┐       ┌─────────────────────────────┐
│         Control Plane           │       │        Runtime Agent         │
│                                 │       │                              │
│  - Multi-tenant management      │◄─────►│  - Executes integrations     │
│  - Integration registry         │  API  │  - Fetches secret bundles    │
│  - Secrets management           │       │  - Reports execution results │
│  - Agent token issuance         │       │  - Runs on-premise           │
│  - Scheduling decisions         │       │                              │
└─────────────────────────────────┘       └─────────────────────────────┘
         ▲
         │  Browser
         ▼
┌─────────────────────────────────┐
│          React Frontend         │
│  (served as static files from   │
│   the control plane in prod)    │
└─────────────────────────────────┘
```

This separation is a first-class design decision. The control plane owns all state and decisions. The runtime agent is stateless — it polls or is triggered by the control plane, executes work, and reports back. This enables a self-hosted model where the agent runs inside the customer's network with access to internal systems, while the control plane can be cloud-hosted.

---

## Control Plane

### Stack

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 10 / ASP.NET Core |
| API style | Minimal APIs |
| Architecture | Vertical Slice Architecture (VSA) |
| ORM | EF Core 10 + Npgsql |
| Database | PostgreSQL |
| Auth | JWT Bearer (user) + agent tokens (runtime) |
| Encryption | AES-256-CBC, random IV per value, PBKDF2 key derivation |
| Password hashing | BCrypt |
| JSON serialization | System.Text.Json with `JsonStringEnumConverter` (enums as strings) |

### Vertical Slice Architecture

Each feature lives in its own folder under `src/ControlPlane/Features/`. A slice contains everything needed for that feature:

```
Features/
  Integrations/
    CreateIntegration.cs      # Command + Handler + Repository interface + Result
    GetIntegration.cs
    ListIntegrations.cs
    UpdateIntegration.cs
    DeleteIntegration.cs
    IntegrationRepository.cs  # EF Core implementation
    IntegrationEndpoints.cs   # Minimal API route registration
```

**Rules:**
- Handlers never access infrastructure directly — always through interfaces registered with DI
- No cross-feature dependencies — features communicate via the dispatcher if needed
- No MediatR — a simple custom dispatcher resolves handlers by type from the DI container

### Command Dispatcher

```csharp
public class Dispatcher(IServiceProvider services) : IDispatcher
{
    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct = default)
    {
        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResult));
        dynamic handler = services.GetRequiredService(handlerType);
        return handler.HandleAsync((dynamic)command, ct);
    }
}
```

### Authentication

Two auth mechanisms exist:

1. **User JWT** — issued on login/setup, contains `sub`, `email`, `tenant_id`, `role` claims. Used for all user-facing API calls. Validated by ASP.NET Core JWT Bearer middleware.

2. **Agent tokens** — format `agt_<base64url(32 random bytes)>`. SHA-256 hash stored in the database. Presented via `X-Agent-Token` header. Validated inline in the agent endpoint. Scoped to a single tenant + environment.

### Secrets

- Values are encrypted with AES-256 before storage
- A random 16-byte IV is generated per value and prepended to the ciphertext
- The encryption key is derived from a master key in config using PBKDF2 (100,000 iterations, SHA-256)
- Secret values are **never returned** through the user-facing API — only keys and metadata
- The secret bundle endpoint (`GET /api/agent/secrets/{environment}`) decrypts and returns all values for an environment, but is only accessible with a valid agent token

### Database

PostgreSQL via EF Core. Migrations are applied automatically on startup — no manual migration step is needed.

Schema:

```
tenants           — id, name, slug, status
users             — id, tenant_id, email, password_hash, role
secrets           — id, tenant_id, environment, key, encrypted_value
integrations      — id, tenant_id, name, slug, description, environment, status, trigger_type, cron_expression, class_name
agent_tokens      — id, tenant_id, name, environment, token_hash
execution_records — id, tenant_id, integration_id, environment, status, started_at, completed_at, error_message
execution_logs    — id, tenant_id, execution_record_id, timestamp, level, message, exception, properties_json
assembly_packages — id, tenant_id, name, version, file_name, data, size_bytes, sha256_hash
```

---

## Runtime Agent

The agent is a .NET Worker Service deployed by the customer. It:

1. Authenticates with the control plane using an agent token
2. Polls for work — integrations that are due to run based on their cron schedules
3. Fetches the secret bundle for its environment
4. Loads and executes the integration C# class with secrets injected
5. Reports execution status and any errors back to the control plane

### Configuration

The agent is configured via `appsettings.json` or environment variables:

| Setting | Description |
|---------|-------------|
| `ControlPlaneUrl` | Base URL of the control plane API |
| `AgentToken` | Token in format `agt_xxx` from the control plane UI |
| `Environment` | Environment this agent serves (e.g. `production`) |
| `IntegrationsPath` | Directory containing integration `.dll` files |
| `PollIntervalSeconds` | How often to check for due integrations (default: 30) |
| `MaxConcurrentExecutions` | Maximum parallel integration runs (default: 5) |

### Integration execution model

An integration is a C# class that implements a simple interface:

```csharp
public interface IIntegration
{
    Task RunAsync(IIntegrationContext context, CancellationToken ct);
}
```

`IIntegrationContext` provides:
- `Secrets` — the decrypted key/value map for the environment
- `Logger` — structured logging that feeds back to the control plane
- `Http` — a pre-configured `HttpClient`
- `Execution` — metadata about the current run (IDs, timestamps)

The agent discovers integration classes by scanning a directory for `.dll` files and finding all types that implement `IIntegration`. The control plane stores the fully-qualified class name (e.g. `MyCompany.Integrations.SyncOrdersIntegration`) which the agent uses for exact type lookup.

### Assembly packages

The control plane has an integration package API for tenant-scoped zip archives:

- `POST /api/integration-packages` stores a zip file containing compiled integration DLLs
- `GET /api/integration-packages` lists package metadata
- `GET /api/integration-packages/{id}/download` downloads the stored archive
- `DELETE /api/integration-packages/{id}` removes the stored archive

Packages are validated as zip files, must contain at least one `.dll`, and are stored with size and SHA-256 metadata. Package names and versions are unique per tenant.

Important current limitation: package storage is not yet connected to runtime-agent deployment. Agents still load DLLs from their local `IntegrationsPath`, and operators must copy/extract package contents onto the agent host and restart the agent. Future work should add agent package sync, version pinning, rollback, and recording the package version used for each execution.

### Concurrency and scheduling

The agent implements several safety mechanisms:

- **Concurrency limit** — A configurable `MaxConcurrentExecutions` setting (default: 5) limits how many integrations can run simultaneously using a semaphore
- **In-flight tracking** — Each integration is tracked while executing; if a poll cycle fires while an integration is still running, it will be skipped to prevent overlapping executions
- **Cron-based scheduling** — The agent evaluates each integration's cron expression against the last run time to determine if it's due

Note: Scheduling state (`_lastRun`) is held in memory. Agent restarts will cause integrations to re-evaluate against `DateTime.MinValue`, potentially triggering immediate runs. For multi-instance deployments, consider distributed locking (future enhancement).

### Agent ↔ Control Plane protocol

```
Agent                              Control Plane
  │                                      │
  │── GET /api/agent/integrations ──────►│  (fetch enabled integrations)
  │◄─ { integrations: [...] } ───────────│
  │                                      │
  │── GET /api/agent/secrets/{env} ─────►│  (fetch secret bundle)
  │◄─ { secrets: { KEY: "val" } } ───────│
  │                                      │
  │── POST /api/agent/executions ───────►│  (open execution record)
  │◄─ { executionId, startedAt } ────────│
  │                                      │
  │  [execute integration locally]       │
  │── POST /api/agent/executions/{id}/logs ─►│  (record execution logs)
  │◄─ 204 No Content ────────────────────│
  │                                      │
  │                                      │
  │── PUT /api/agent/executions/{id} ───►│  (close with result)
  │◄─ 204 No Content ────────────────────│
```

The control plane validates execution requests:
- Integration must exist and belong to the agent's tenant
- Integration must match the agent's environment
- Integration must be enabled

---

## Frontend

| Technology | Purpose |
|-----------|---------|
| React 19 | UI framework |
| Vite | Build tool + dev server |
| TypeScript | Type safety |
| React Router v7 | Client-side routing |
| TanStack Query | Server state, caching, mutations |
| shadcn/ui + Tailwind | Component library |

In production, the React app is built to `src/ControlPlane/wwwroot` and served as static files by the .NET server. In development, Vite's dev server runs on port 5173 with API calls proxied to the backend on port 5000.

### Route structure

```
/setup          — First-run setup (public, redirects away if already complete)
/login          — Authentication (public)
/integrations   — Integration registry (protected)
/secrets        — Secrets management (protected)
/agent-tokens   — Agent token management (protected)
```

`ProtectedRoute` checks setup status and auth state before rendering protected routes, redirecting to `/setup` or `/login` as appropriate.

---

## Multi-tenancy

Every resource (user, secret, integration, agent token) is scoped to a `TenantId`. The `ICurrentUser` service extracts the tenant from the JWT claims on every request. Handlers receive `TenantId` as an explicit parameter and all database queries filter by it — there is no global tenant context or ambient state.

The database has a single schema shared across tenants (not schema-per-tenant). Row-level isolation is enforced in application code.

---

## Deployment

### Self-hosted (current)

The entire stack runs on a single host via Docker Compose:

```
docker-compose.yml
  ├── postgres   — database
  └── app        — control plane + frontend
```

Secrets (JWT key, encryption key, DB password) are supplied via a `.env` file.

### Hybrid (future)

```
Customer network                    Cloud (or customer VPS)
┌──────────────────┐               ┌──────────────────────┐
│  Runtime Agent   │◄─────────────►│    Control Plane      │
│  (on-premise)    │  HTTPS        │    (cloud-hosted)     │
└──────────────────┘               └──────────────────────┘
```

The control plane can be hosted centrally (SaaS) while the runtime agent runs inside the customer's network. The agent only needs outbound HTTPS access to the control plane — no inbound firewall rules required.
