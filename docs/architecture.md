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
tenants          — id, name, slug, status
users            — id, tenant_id, email, password_hash, role
secrets          — id, tenant_id, environment, key, encrypted_value
integrations     — id, tenant_id, name, slug, description, environment, status, trigger_type, cron_expression
agent_tokens     — id, tenant_id, name, environment, token_hash
```

---

## Runtime Agent

> Not yet built. Design intent documented here to guide implementation.

The agent is a separate process deployed by the customer. It:

1. Authenticates with the control plane using an agent token
2. Polls (or is pushed) for work — integrations that are due to run
3. Fetches the secret bundle for its environment
4. Loads and executes the integration C# class with secrets injected
5. Reports execution status, logs, and any output back to the control plane

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
- (future) `Trigger` — event payload if triggered by webhook

The agent discovers integration classes by loading a compiled assembly. The control plane stores or references the assembly — exact mechanism TBD (options: assembly uploaded to control plane, Git repo URL, NuGet package).

### Agent ↔ Control Plane protocol

```
Agent                          Control Plane
  │                                  │
  │── GET /api/agent/poll ──────────►│  (heartbeat + fetch pending work)
  │◄─ { integrations: [...] } ───────│
  │                                  │
  │── GET /api/agent/secrets/prod ──►│  (fetch secret bundle)
  │◄─ { secrets: { KEY: "val" } } ───│
  │                                  │
  │  [execute integration locally]   │
  │                                  │
  │── POST /api/agent/executions ───►│  (report result)
  │◄─ 200 OK ────────────────────────│
```

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
