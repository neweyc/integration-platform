# Integration Platform

A code-first integration platform. Integrations are written as C# classes rather than configured via low-code drag-and-drop.

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
    "PollIntervalSeconds": 30,
    "MaxConcurrentExecutions": 5
  }
}
```

### Running

```bash
dotnet run --project src/RuntimeAgent
```

The agent will:
1. Load integration assemblies from `IntegrationsPath`
2. Poll for enabled integrations matching its environment
3. Execute due integrations based on their cron schedules
4. Report execution results back to the control plane

### Deploying integrations

The runtime agent currently loads assemblies from the local filesystem. Package upload APIs exist in the control plane for storing and downloading zip archives, but agents do not automatically sync packages yet.

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
3. Copy the published DLLs and dependencies to the agent's `IntegrationsPath` directory
4. Restart the agent to load new assemblies
5. Register the integration in the control plane UI with the fully qualified class name

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

Currently 90+ tests covering control plane features, SDK, and runtime agent behavior.
