# Quick start

Serto runs integrations you write as C# — on a schedule, from a webhook, or on demand. This guide takes you from nothing to a running integration.

## 1. Run the control plane

The control plane is the API and dashboard that stores your integrations, schedules them, and holds their secrets. Self-host it with Docker — the [self-host guide](/install) has a copy-paste `docker-compose.yml`.

Once it's up, open `http://localhost:8080` and create your admin account.

## 2. Create a project

Install the CLI and scaffold a project:

```bash
serto init my-integrations
cd my-integrations
```

You get a normal .NET project that references the `Serto.Sdk` package.

## 3. Write an integration

An integration is a C# class that implements `IIntegration` and carries a trigger attribute:

```csharp
[ScheduledIntegration("Order Sync", "order-sync", "*/15 * * * *")]
public class OrderSync : IIntegration
{
    public async Task RunAsync(IIntegrationContext ctx, CancellationToken ct)
    {
        ctx.Logger.LogInformation("Running order sync...");
        // your logic here
    }
}
```

The attribute *is* the manifest. `serto deploy` reads it and provisions the schedule for you — no visual designer, no click-ops.

## 4. Deploy

```bash
serto deploy --url http://localhost:8080
```

The CLI packages your integrations and uploads them. Open the dashboard and you'll see **Order Sync** scheduled and ready — its next run, execution history, and logs all live there.

## Next steps

- [Writing integrations](/docs/writing-integrations) — triggers, the context, connectors, and secrets.
- [Architecture](/docs/architecture) — how the control plane and runtime agents fit together.
