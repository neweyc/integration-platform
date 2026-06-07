# Serto

A **Developer Integration Platform** built on the principle of **Integration-as-Code**. We replace legacy, opaque IPaaS "Low-Code" designers with a high-velocity workflow developers actually love.

## Product direction

We provide the **magic developer experience** of Vercel or Temporal for enterprise C# integrations. We eliminate "Click-Ops" by allowing the **Code to be the Manifest**.

### The Integration-as-Code Workflow:
1. **Author:** Write a C# class and decorate it with `[ScheduledIntegration]`.
2. **Develop:** `serto dev` watches your code and runs tests locally on every save.
3. **Deploy:** `serto deploy`. The Control Plane auto-provisions the infrastructure based on your code.

We are the **"Terraform for Enterprise Integrations,"** designed specifically for engineering teams who need to solve complex SAP, Salesforce, and internal API automation without proprietary lock-in.

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

## Documentation

- [Installation Guide](docs/installation.md) — local dev, Docker/production, configuration reference
- [Quick Start](docs/quickstart.md) — run the platform and deploy your first integration in ~10 minutes
- [Writing integrations](docs/writing-integrations.md) — SDK, triggers, connectors, and secrets
- [API reference](docs/api-reference.md) — control-plane HTTP API
- [Architecture](docs/architecture.md) — how the control plane, agent, and CLI fit together

The sections below are a condensed version of the [Installation Guide](docs/installation.md).

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
| Database | `integration_platform` |
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

The runtime agent loads assemblies from the local filesystem and can sync uploaded packages from the control plane. Synced packages are downloaded, SHA-256 verified, extracted under `PackagesPath`, and loaded by the agent. Integrations can be pinned to uploaded package versions so future executions use deterministic code.

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
4. Register or update the integration in the control plane with the fully qualified class name and package version

The CLI can scan, package, and upload the current integration project:

```bash
dotnet run --project src/Cli -- scan

dotnet run --project src/Cli -- package \
  --name MyCompany.Integrations \
  --version 1.0.0

SERTO_API_TOKEN=<personal-access-token> dotnet run --project src/Cli -- deploy \
  --url http://localhost:5000 \
  --name MyCompany.Integrations \
  --version 1.0.0
```

`serto scan`, `serto package`, and `serto deploy` all show the same discovery preview: package metadata, decorated integration classes, trigger declarations, run policy, validation errors, and required secret names detected from connector/context usage. If `--version` is omitted, `serto deploy` uses `PackageVersion` or `Version` from the project file and falls back to a timestamped development version. You can still copy published DLLs directly to the agent's `IntegrationsPath` for local development. Package sync avoids manual copying; integrations can be pinned to uploaded package versions, and rollback is performed by repointing the integration to a previous package version.

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
docker build -f src/ControlPlane/Dockerfile -t serto .

# Run with environment variables
docker run -p 8080:8080 \
  -e ConnectionStrings__DefaultConnection="Host=db;Database=integrationplatform;Username=user;Password=pass" \
  -e Jwt__Key="your-256-bit-secret-key" \
  -e Encryption__MasterKey="your-encryption-master-key" \
  serto
```

The Dockerfile builds both the React frontend and .NET backend in a multi-stage build.

For production, use Docker Compose or Kubernetes with proper secrets management.

## Running tests

```bash
scripts/validate.sh
```

This runs patch hygiene checks, .NET restore/build/test, and frontend lint/build when `node_modules` is present.

Currently 266 tests cover control plane features, SDK behavior, runtime agent behavior, CLI behavior, API contracts, RBAC/security boundaries, audit logging, commercial API surfaces, and database-backed integration paths.

The control plane integration tests use a temporary PostgreSQL database when one is available. By default they try the local development database server at `127.0.0.1:5433` with `devuser` / `devpassword`. To point them at a different server, set:

```bash
export INTEGRATION_TEST_CONNECTION="Host=127.0.0.1;Port=5433;Database=postgres;Username=devuser;Password=devpassword"
dotnet test
```

The test harness creates and drops isolated databases named `integration_platform_test_*`.

See [docs/correctness-controls.md](docs/correctness-controls.md) for the CI gates and review checklist used to keep feature work honest.
