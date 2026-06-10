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
| `serto init [name]` | Scaffold a new integration project + unit-test project, README, and secrets example (`--template scheduled`\|`webhook`) |
| `serto scan` | Preview integrations/triggers/required-secrets discovered from the current project |
| `serto package` | Build, validate, SHA-256, and archive the project (no upload) |
| `serto login` | Save an API token for a control plane so `serto deploy` stops prompting |
| `serto logout` | Remove a saved API token (`--all` clears every saved control plane) |
| `serto deploy` | Run the scan preview, then upload and auto-provision in the control plane |
| `serto test` | Validate (attribute, cron, constructor, required secrets) and run an integration locally |
| `serto dev` | Watch source files and re-run tests on save |
| `serto webhook replay` | Sign and POST a sample webhook payload to a running control plane |
| `serto --version` | Print the installed CLI version |

### Authenticating the CLI

`serto deploy` needs a personal access token (create one in the UI under **Access tokens**). Rather than
passing it every time, run `serto login` once per control plane:

```bash
serto login --url https://your-control-plane.example.com
# paste your pat_… token when prompted (input is hidden)
```

The token is verified against the control plane and then saved to `~/.serto/credentials.json`, keyed by
control-plane URL. On macOS/Linux the file is written with owner-only permissions (`chmod 600`); the token
is a revocable, tenant-scoped credential. `serto logout` removes it (`--all` clears every saved control
plane).

The control plane you logged into most recently becomes the **default**, so `serto deploy` targets it
automatically — you don't need to repeat `--url`. (Pass `--url` to deploy to a different saved control
plane; `serto login` again to switch the default.)

`serto deploy` resolves the **URL** as: explicit `--url` → your last `serto login` → `http://localhost:5000`.
It resolves the **token** in this order, so saved credentials are a convenience that flags and CI
environment variables always override:

1. `--token` flag
2. `SERTO_API_TOKEN` / `IP_API_TOKEN` environment variables (use these in CI)
3. Saved credentials for the resolved URL
4. Interactive prompt

To get a real `serto` command instead of `dotnet run --project src/Cli --`, install the CLI as a .NET global tool after it has been published to NuGet:

```bash
dotnet tool install --global Serto.Cli   # add --version X.Y.Z to pin a specific release
serto --version
```

For local development before publishing the tool package, publish it and put it on your `PATH`:

```bash
dotnet publish src/Cli -c Release -o ~/.serto
# then add an alias, e.g. in ~/.zshrc:
alias serto='dotnet ~/.serto/Cli.dll'
```

> **Note:** `serto init` scaffolds a project that references the published `Serto.Sdk`, `Serto.Connectors`, and `Serto.Testing` NuGet packages. The current package version is `1.0.18`:
>
> ```xml
> <ItemGroup>
>   <PackageReference Include="Serto.Sdk" Version="1.0.18" />
>   <PackageReference Include="Serto.Connectors" Version="1.0.18" />
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

For production, use the published Docker images instead of building from source on the server. A typical deployment runs the control plane on one host, PostgreSQL on a managed database or separate database host, and runtime agents on one or more hosts inside the networks that contain the systems your integrations need to reach.

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
SERTO_CONTROL_PLANE_CONNECTION_STRING="Host=db.example.com;Database=integration_platform;Username=platform;Password=<strong-password>"
```

### 2. Start the control plane

```bash
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d
```

- Control plane: `http://localhost:8080`

Open `http://localhost:8080` and complete `/setup`.

### 3. Run agents from Docker

Create an agent token in the UI, then add it to `.env`:

```ini
SERTO_AGENT_TOKEN=agt_<your-token>
SERTO_AGENT_ENVIRONMENT=production
SERTO_AGENT_CONTROL_PLANE_URL=https://your-control-plane.example.com
```

On each agent host:

```bash
docker compose -f docker-compose.agent.yml pull
docker compose -f docker-compose.agent.yml up -d
```

### Single-host trial

For a quick trial on one machine, use the trial compose file. It runs PostgreSQL and the control plane together:

```bash
docker compose -f docker-compose.trial.yml pull
docker compose -f docker-compose.trial.yml up -d
```

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
| `Zepto:Token` | `Zepto__Token` | ZeptoMail "Send Mail" token for the platform-default email sender used by failure alerts. Leave blank to disable the email default. |
| `Zepto:FromAddress` | `Zepto__FromAddress` | Verified sender address platform-sent alert emails come from. Required for the email default to work. |
| `Zepto:FromName` | `Zepto__FromName` | Display name on platform-sent alert emails (defaults to `Serto Alerts`). |
| `Zepto:BaseUrl` | `Zepto__BaseUrl` | ZeptoMail API endpoint. Override for the EU data center. Defaults to `https://api.zeptomail.com/v1.1/email`. |
| `AlertWebhooks:AllowPrivateNetworkTargets` | `AlertWebhooks__AllowPrivateNetworkTargets` | When `false` (default), alert webhook URLs that resolve to private/loopback/link-local/metadata addresses are blocked (SSRF protection). Set `true` only on self-hosted deployments that deliberately post alerts to internal endpoints. |

The shipped `appsettings.json` contains `CHANGE-THIS-...` placeholders for `Jwt:Secret` and `Encryption:MasterKey`; always override them in any non-development environment.

### Failure-alert email (ZeptoMail)

Failure alerts can notify by email and/or an outbound webhook. The webhook channel needs no server-side configuration — a tenant just enters a URL. Email is sent through **ZeptoMail** (Zoho's transactional email) by default, configured once by the operator via the `Zepto:*` settings above; tenants then only enable email and list recipients. A tenant that needs alerts to come from its own domain can instead configure its own SMTP server in the UI (**Alerts** page), which takes precedence over the platform default for that tenant. If neither ZeptoMail nor a tenant SMTP server is configured, email alerts are simply not sent (the webhook channel still works). In Docker, set `SERTO_ZEPTO_TOKEN` and `SERTO_ZEPTO_FROM_ADDRESS` in your `.env`.

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
- **Scaffolded integration won't restore `Serto.Sdk`** — confirm NuGet.org is reachable and that the generated project references a published package version such as `1.0.18`.

---

## Next steps

- [Quick Start](quickstart.md) — author and deploy your first integration end to end
- [Writing integrations](writing-integrations.md) — the SDK, triggers, connectors, and secrets
- [API reference](api-reference.md) — control-plane HTTP API
- [Architecture](architecture.md) — how the pieces fit together
