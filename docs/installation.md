# Installation Guide

This guide covers running Serto for local development and deploying it with Docker. If you just want to get something running fast, start with the [Quick Start](quickstart.md) and come back here for the details.

Serto has three runnable parts:

| Component | What it is | Default URL |
|-----------|------------|-------------|
| **Control plane** | ASP.NET Core API + React UI. Stores integrations, packages, secrets, schedules, and audit/RBAC. | `http://localhost:5000` (dev) / `:8080` (Docker) |
| **Runtime agent** | A separate process that polls the control plane, syncs packages, and executes your integration code. | — (outbound only) |
| **CLI (`serto`)** | Scaffolds, scans, packages, and deploys integration projects. | — |

---

## Prerequisites

- **.NET 10 SDK** — `dotnet --version` should report `10.x`
- **Node.js 20+** and npm — only needed to run/build the React UI
- **Docker** (with Compose v2) — used for PostgreSQL locally and for the production image
- **PostgreSQL 16** — provided via Docker Compose below; no separate install needed

Optional: `psql` or pgAdmin for inspecting the database, `curl` for API calls.

---

## Local development

### 1. Start PostgreSQL

```bash
docker compose -f docker-compose.dev.yml up -d
```

This runs PostgreSQL 16 on **`localhost:5433`** (mapped from the container's 5432 to avoid clashing with a local Postgres) with:

| Field | Value |
|-------|-------|
| Database | `integration_platform` |
| Username | `devuser` |
| Password | `devpassword` |
| Host / Port | `localhost` / `5433` |

`src/ControlPlane/appsettings.Development.json` is already pointed at this instance, so no configuration is needed.

### 2. Start the control plane

```bash
dotnet run --project src/ControlPlane
```

Runs on `http://localhost:5000`. **Migrations are applied automatically on startup**, so the schema is created on first run.

### 3. Start the frontend dev server

```bash
cd src/ControlPlane.Client
npm install      # first time only
npm run dev
```

Runs on **`http://localhost:5173`** and proxies API calls to the control plane. Use port **5173** during development (hot module replacement) rather than 5000.

> The .NET server can also serve the built SPA from `wwwroot` at `:5000`, but during development the Vite dev server on `:5173` gives you instant reloads.

### 4. Create the first tenant and admin

Open `http://localhost:5173`. You'll be redirected to **`/setup`** to create the first tenant and admin user. After that you can sign in and use the UI.

---

## Building and using the CLI

The CLI project is `src/Cli`. You can run it directly:

```bash
dotnet run --project src/Cli -- <command>
```

Available commands:

| Command | Purpose |
|---------|---------|
| `serto init [name]` | Scaffold a new integration project |
| `serto scan` | Preview integrations/triggers/required-secrets discovered from the current project |
| `serto package` | Build, validate, SHA-256, and archive the project (no upload) |
| `serto deploy` | Run the scan preview, then upload and auto-provision in the control plane |
| `serto test` | Run an integration locally |
| `serto dev` | Watch source files and re-run tests on save |
| `serto webhook replay` | Sign and POST a sample webhook payload to a running control plane |

To get a real `serto` command instead of `dotnet run --project src/Cli --`, publish it and put it on your `PATH`:

```bash
dotnet publish src/Cli -c Release -o ~/.serto
# then add an alias, e.g. in ~/.zshrc:
alias serto='dotnet ~/.serto/Cli.dll'
```

> **Note:** `serto init` scaffolds a project that references the published `Serto.Sdk` and `Serto.Connectors` NuGet packages. The current package version is `1.0.3`:
>
> ```xml
> <ItemGroup>
>   <PackageReference Include="Serto.Sdk" Version="1.0.3" />
>   <PackageReference Include="Serto.Connectors" Version="1.0.3" />
> </ItemGroup>
> ```

---

## Running the runtime agent

The runtime agent executes your integrations. It needs an **agent token** to authenticate to the control plane and fetch secrets.

### 1. Create an agent token

In the UI: **Agent tokens → New token**. Give it a name and an environment (e.g. `production`). The token is shown **once** — copy it. A token is scoped to a single environment (a `production` token cannot read `staging` secrets).

### 2. Configure the agent

Set the agent's configuration (via `src/RuntimeAgent/appsettings.json`, user secrets, or environment variables):

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

### 3. Run it

```bash
dotnet run --project src/RuntimeAgent
```

The agent loads local assemblies from `IntegrationsPath`, syncs uploaded packages into `PackagesPath` (downloaded, SHA-256 verified, extracted), polls for claimed due work in its environment, executes it, and reports results back. Scheduling state lives in the control plane, so restarting the agent does not reset cron evaluation.

---

## Production Deployment (Docker)

For production, use the published Docker images instead of building from source on the server. The production compose file runs PostgreSQL and the published control-plane image. The runtime agent can also be run from its published image when the Docker host has access to the systems your integrations need to reach.

### 1. Create a `.env`

```bash
cp .env.example .env
```

Fill in the values. Never commit `.env`:

```ini
POSTGRES_USER=platform
POSTGRES_PASSWORD=<strong-password>

# Generate strong random values:
#   openssl rand -base64 32
JWT_SECRET=<random, min 32 chars>
ENCRYPTION_MASTER_KEY=<random>

SERTO_IMAGE_TAG=1.0.3
```

### 2. Start the control plane

```bash
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d
```

- Control plane: `http://localhost:8080`
- PostgreSQL: `localhost:5432`

Open `http://localhost:8080` and complete `/setup`.

### 3. Run an agent from Docker

Create an agent token in the UI, then add it to `.env`:

```ini
SERTO_AGENT_TOKEN=agt_<your-token>
SERTO_AGENT_ENVIRONMENT=production
```

For a single-host trial, start the agent profile:

```bash
docker compose -f docker-compose.prod.yml --profile agent up -d
```

In production, the agent should run wherever it has network access to the systems the integrations use. That might be the same Docker host, but it is often a separate host inside a private network. If the agent is not running in the same compose network as the control plane, set `SERTO_AGENT_CONTROL_PLANE_URL` to the externally reachable control-plane URL.

> ⚠️ **Two secrets you must set carefully and not change later:**
> - **`ENCRYPTION_MASTER_KEY`** encrypts all stored secrets. Changing it after secrets exist makes every stored secret undecryptable. Set it once, back it up securely.
> - **`JWT_SECRET`** signs auth tokens. Rotating it invalidates all existing sessions/tokens (users must sign in again).

---

## Configuration reference

The control plane reads standard ASP.NET Core configuration. Any setting can be overridden by an environment variable using `__` (double underscore) as the section separator.

| Setting | Env var | Description |
|---------|---------|-------------|
| `ConnectionStrings:Default` | `ConnectionStrings__Default` | PostgreSQL connection string |
| `Jwt:Secret` | `Jwt__Secret` | HMAC signing key for auth tokens (min 32 chars) |
| `Jwt:Issuer` | `Jwt__Issuer` | Token issuer |
| `Jwt:Audience` | `Jwt__Audience` | Token audience |
| `Jwt:ExpiryHours` | `Jwt__ExpiryHours` | Access-token lifetime in hours |
| `Encryption:MasterKey` | `Encryption__MasterKey` | Master key used to derive the secret-encryption key |

The shipped `appsettings.json` contains `CHANGE-THIS-...` placeholders for `Jwt:Secret` and `Encryption:MasterKey`; always override them in any non-development environment.

### Frontend production build

The control plane serves the compiled SPA from `wwwroot`. To produce it:

```bash
cd src/ControlPlane.Client
npm run build   # outputs to src/ControlPlane/wwwroot
```

---

## Troubleshooting

- **Port 5433 already in use** — another Postgres is bound there. Stop it, or change the host port in `docker-compose.dev.yml` and the `Port=` in `appsettings.Development.json`.
- **Control plane can't connect to the database** — confirm the dev container is healthy (`docker compose -f docker-compose.dev.yml ps`) and that the connection string host/port match (`127.0.0.1:5433`).
- **UI loads but API calls fail in dev** — make sure the control plane is running on `:5000`; the Vite dev server on `:5173` proxies to it.
- **`/setup` doesn't appear** — setup is only offered until the first tenant exists. If a tenant was already created, go to the login page instead.
- **Scaffolded integration won't restore `Serto.Sdk`** — confirm NuGet.org is reachable and that the generated project references a published package version such as `1.0.3`.

---

## Next steps

- [Quick Start](quickstart.md) — author and deploy your first integration end to end
- [Writing integrations](writing-integrations.md) — the SDK, triggers, connectors, and secrets
- [API reference](api-reference.md) — control-plane HTTP API
- [Architecture](architecture.md) — how the pieces fit together
