# Quick Start

Get Serto running and execute your first integration. This takes about 10 minutes and assumes you've cloned the repo. For deeper setup options (Docker, production, configuration), see the [Installation Guide](installation.md).

**Prerequisites:** .NET 10 SDK, Node.js 20+, and Docker. Run all commands from the repository root unless noted.

---

## 1. Start the platform

Start PostgreSQL, the control plane, and the UI in three terminals:

```bash
# Terminal 1 — database
docker compose -f docker-compose.dev.yml up -d

# Terminal 2 — control plane (migrations apply automatically)
dotnet run --project src/ControlPlane

# Terminal 3 — web UI
cd src/ControlPlane.Client && npm install && npm run dev
```

Open **`http://localhost:5173`**. You'll land on **`/setup`** — create your first tenant and admin user, then sign in.

---

## 2. Get a deploy token

In the UI, open the **Developer** tab and generate a **personal access token** (it starts with `pat_`). Copy it — you'll use it to deploy from the CLI.

```bash
export SERTO_API_TOKEN=pat_...
```

---

## 3. Write an integration

Create a small project at the repo root. Because the `Serto.Sdk` NuGet packages aren't published yet, reference the in-repo SDK projects directly.

```bash
mkdir my-integrations && cd my-integrations
```

`my-integrations.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../src/Sdk/Sdk.csproj" />
    <ProjectReference Include="../src/Connectors/Connectors.csproj" />
  </ItemGroup>
</Project>
```

`HelloIntegration.cs`:

```csharp
using Serto.Sdk;
using Microsoft.Extensions.Logging;

namespace MyIntegrations;

// Runs every hour. The slug "hello" is the stable identity the control plane provisions.
[ScheduledIntegration("Hello", "hello", "0 * * * *")]
public class HelloIntegration : IIntegration
{
    public Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        context.Logger.LogInformation(
            "Hello from {Name}! Running in {Env}.",
            context.Execution.IntegrationName,
            context.Execution.Environment);
        return Task.CompletedTask;
    }
}
```

Build it:

```bash
dotnet build
```

---

## 4. Preview and deploy

From inside `my-integrations/`, preview what the control plane will discover:

```bash
dotnet run --project ../src/Cli -- scan
```

You'll see the discovered integration, its `hello` scheduled trigger, the run policy, and any required secrets.

Now deploy. This packages the project, uploads it, and auto-provisions the integration and its trigger:

```bash
dotnet run --project ../src/Cli -- deploy --url http://localhost:5000
```

The output reports the package version, the provisioned integration/trigger (with the next scheduled run), and a **secret check** of required vs. configured secrets. Refresh the UI's **Integrations** page — `Hello` is now listed.

---

## 5. Run an agent to execute it

The control plane schedules work; a **runtime agent** executes it.

1. In the UI, open **Agent tokens → New token**, set the environment to `production`, and copy the token (`agt_...`, shown once).
2. Point the agent at the control plane. The quickest way is environment variables:

```bash
# Terminal 4 — runtime agent
export Agent__ControlPlaneUrl=http://localhost:5000
export Agent__AgentToken=agt_...
export Agent__Environment=production
export Agent__IntegrationsPath=./packages
export Agent__PackagesPath=./packages
dotnet run --project src/RuntimeAgent
```

The agent downloads your uploaded package (SHA-256 verified), then polls for due work. It executes the integration when its schedule is due.

---

## 6. Trigger a run and watch it

You don't have to wait for the cron schedule — in the UI, open the integration and click **Run now**. Within a poll interval the agent claims and executes it. Open the integration's **execution history** to see the run, its status, and the `Hello from Hello!` log line.

---

## What just happened

- Your **code is the manifest**: the `[ScheduledIntegration]` attribute declared the integration and its schedule, and `serto deploy` provisioned it — no click-ops.
- The **control plane** owns scheduling, secrets, and operational state; the **agent** runs close to your systems and only needs an outbound connection.
- Re-deploying preserves operator changes: if you disable the trigger or change its cron in the UI, a later `serto deploy` keeps your override and reports the difference as drift.

## Next steps

- [Writing integrations](writing-integrations.md) — triggers (scheduled, webhook, manual, queue), connectors (HTTP, SQL), secrets, and retries
- [Installation Guide](installation.md) — Docker/production deployment and full configuration
- [API reference](api-reference.md) — the control-plane HTTP API
