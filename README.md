# Integration Platform

A code-first workflow automation and integration platform. Integrations are written as C# classes rather than configured via low-code drag-and-drop.

## Product direction

The product goal is to replace the common 60-70% of Control-M/Boomi usage that is really scheduled jobs, data movement, API calls, transformations, retries, observability, and environment-safe deployment, while staying code-first.

This is not intended to be a low-code designer clone. The platform should give engineering-led teams real code, tests, packages, versioned deployment, rollback, agent execution close to systems/data, and an operations UI for scheduling, visibility, retries, audit, and administration.

Near-term product priorities:

1. Version-pinned package execution
2. Work item queue
3. Workflow DAG foundation
4. Retry policy
5. Agent heartbeats and agent pools
6. Webhook triggers
7. Core connectors for HTTP, SQL, files/SFTP, object storage, and notifications
8. Code-first transform steps
9. Environment promotion
10. Audit and RBAC

## Stack

- **Backend**: ASP.NET Core 10, EF Core, PostgreSQL
- **Frontend**: React 19, Vite, TypeScript, TanStack Query, shadcn/ui, Tailwind

## Local development

### Prerequisites

- .NET 10 SDK
- Node.js 20+
- Docker

### 1. Start the database

```bash
docker-compose -f docker-compose.dev.yml up -d
```

### 2. Start the API

```bash
$HOME/.dotnet/dotnet run --project src/ControlPlane
```

Runs on `http://localhost:5000`. Migrations are applied automatically on startup.

### 3. Start the frontend dev server

```bash
cd src/ControlPlane.Client && npm run dev
```

Runs on `http://localhost:5173`. API calls are proxied to the backend automatically.

> **Note**: Use port 5173 during development, not 5000. Hot module replacement means changes are reflected instantly without rebuilding.

### First run

Navigate to `http://localhost:5173` — you'll be directed to `/setup` to create the first tenant and admin user.

## Connecting to the database (pgAdmin)

| Field    | Value                  |
|----------|------------------------|
| Host     | `localhost`            |
| Port     | `5433`                 |
| Database | `integrationplatform`  |
| Username | `devuser`              |
| Password | `devpassword`          |

Port is `5433` (not the default 5432) to avoid conflicts with any local Postgres instance.

## Agent tokens

Agent tokens allow a runtime agent to fetch decrypted secrets from the control plane. They are scoped to a single environment — a `production` token cannot access `staging` secrets.

### Creating a token

1. Go to **Agent tokens** in the UI and click **New token**
2. Give it a name and set the environment (e.g. `production`)
3. Copy the token value — it is shown **once only** and cannot be retrieved again

### Using a token

The agent calls the secret bundle endpoint with the token in the `X-Agent-Token` header:

```
GET /api/agent/secrets/{environment}
X-Agent-Token: agt_<token>
```

Response is a decrypted key/value map:

```json
{
  "secrets": {
    "DATABASE_URL": "postgres://...",
    "API_KEY": "sk-..."
  }
}
```

The agent injects these into the job's execution environment before running an integration.

## Running the Runtime Agent

The runtime agent is a separate process that executes integrations. It polls the control plane for work, fetches secrets, and runs your C# integration classes.

### Configuration

Create an `appsettings.json` in the RuntimeAgent project directory:

```json
{
  "Agent": {
    "ControlPlaneUrl": "http://localhost:5000",
    "AgentToken": "agt_<your-token>",
    "Environment": "production",
    "IntegrationsPath": "./integrations",
    "PackagesPath": "./packages",
    "PollIntervalSeconds": 30,
    "PackageSyncIntervalSeconds": 300,
    "MaxConcurrentExecutions": 5,
    "ShutdownDrainSeconds": 30
  }
}
```

### Running

```bash
dotnet run --project src/RuntimeAgent
```

The agent will:
1. Load integration assemblies from `IntegrationsPath`
2. Sync uploaded integration packages into `PackagesPath`
3. Poll the control plane for claimed due integrations matching its environment
4. Execute the integrations returned by the control plane
5. Report execution results back to the control plane

Scheduling state is persisted in the control plane, so agent restarts do not reset cron evaluation.

### Deploying integrations

The runtime agent loads assemblies from the local filesystem and can sync uploaded packages from the control plane. Synced packages are downloaded, SHA-256 verified, extracted under `PackagesPath`, and loaded by the agent. Package version pinning is not implemented yet, so uploaded packages are tenant-scoped and every agent for that tenant can discover them.

1. Build your integration project: `dotnet publish -c Release`
2. Zip the publish output if you want to store the package in the control plane:
   ```bash
   cd bin/Release/net10.0/publish
   zip -r integrations.zip .
   curl -X POST http://localhost:5000/api/integration-packages \
     -H "Authorization: Bearer <jwt>" \
     -F "name=MyCompany.Integrations" \
     -F "version=1.0.0" \
     -F "file=@integrations.zip"
   ```
3. The agent will download and extract the package on startup or at the next package sync interval
4. Register the integration in the control plane UI with the fully qualified class name

You can still copy published DLLs directly to the agent's `IntegrationsPath` for local development. Package sync avoids manual copying, but integrations are not yet pinned to package versions and rollback is still manual.

See [docs/writing-integrations.md](docs/writing-integrations.md) for details.

## Building the frontend for production

```bash
cd src/ControlPlane.Client && npm run build
```

Output goes to `src/ControlPlane/wwwroot` and is served by the .NET server at `http://localhost:5000`.

## Docker deployment

Build and run the control plane as a Docker container:

```bash
# Build from repository root
docker build -f src/ControlPlane/Dockerfile -t integration-platform .

# Run with environment variables
docker run -p 8080:8080 \
  -e ConnectionStrings__DefaultConnection="Host=db;Database=integrationplatform;Username=user;Password=pass" \
  -e Jwt__Key="your-256-bit-secret-key" \
  -e Encryption__MasterKey="your-encryption-master-key" \
  integration-platform
```

The Dockerfile builds both the React frontend and .NET backend in a multi-stage build.

For production, use Docker Compose or Kubernetes with proper secrets management.

## Running tests

```bash
dotnet test
```

Currently 125 tests covering control plane features, SDK, runtime agent behavior, and database-backed integration paths.

The control plane integration tests use a temporary PostgreSQL database when one is available. By default they try the local development database server at `127.0.0.1:5433` with `devuser` / `devpassword`. To point them at a different server, set:

```bash
export INTEGRATION_TEST_CONNECTION="Host=127.0.0.1;Port=5433;Database=postgres;Username=devuser;Password=devpassword"
dotnet test
```

The test harness creates and drops isolated databases named `integration_platform_test_*`.
