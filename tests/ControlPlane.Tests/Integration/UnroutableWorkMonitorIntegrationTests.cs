using ControlPlane.Features.Alerts;
using Shared.Domain;

namespace ControlPlane.Tests.IntegrationTests;

public class UnroutableWorkMonitorIntegrationTests
{
    [Fact]
    public async Task Monitor_AlertsOnceWhenUnroutable_AndClearsOnRecovery()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenant = new Tenant { Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" };
        var integration = new Integration
        {
            TenantId = tenant.Id,
            Name = "Reactor Pulse",
            Slug = "reactor-pulse",
            Environment = "production",
            ClassName = "A.ReactorPulse",
            Status = IntegrationStatus.Enabled,
            RequiredTags = ["hardware-signal"]
        };
        var agentToken = new AgentToken { TenantId = tenant.Id, Name = "floor-1", Environment = "production", TokenHash = Guid.NewGuid().ToString("N") };

        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(tenant);
            TestEnvironments.Seed(db, tenant.Id, "production");
            db.Integrations.Add(integration);
            db.AgentTokens.Add(agentToken);
            await db.SaveChangesAsync();
        }

        var now = DateTime.UtcNow;

        // 1. No live agent offers the tag → newly unroutable, alerted once.
        await using (var db = database.CreateContext())
        {
            var handler = new MonitorUnroutableWorkHandler(new UnroutableAlertRepository(db));
            var result = await handler.HandleAsync(new MonitorUnroutableWorkCommand(now));

            var alert = Assert.Single(result.NewlyUnroutable);
            Assert.Equal("reactor-pulse", alert.Slug);
            Assert.Equal(["hardware-signal"], alert.RequiredTags);
        }

        // The dedup stamp was persisted.
        await using (var db = database.CreateContext())
        {
            var stored = db.Integrations.Single(i => i.Id == integration.Id);
            Assert.NotNull(stored.UnroutableAlertedAt);
        }

        // 2. Still no agent → no repeat alert (deduped).
        await using (var db = database.CreateContext())
        {
            var handler = new MonitorUnroutableWorkHandler(new UnroutableAlertRepository(db));
            var result = await handler.HandleAsync(new MonitorUnroutableWorkCommand(now.AddSeconds(1)));
            Assert.Empty(result.NewlyUnroutable);
        }

        // 3. A live agent offering the tag connects → recovery clears the stamp, no new alert.
        await using (var db = database.CreateContext())
        {
            db.AgentHeartbeats.Add(new AgentHeartbeat
            {
                TenantId = tenant.Id,
                AgentTokenId = agentToken.Id,
                Environment = "production",
                Tags = ["hardware-signal"],
                LastSeenAt = now.AddSeconds(2)
            });
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var handler = new MonitorUnroutableWorkHandler(new UnroutableAlertRepository(db));
            var result = await handler.HandleAsync(new MonitorUnroutableWorkCommand(now.AddSeconds(3)));

            Assert.Empty(result.NewlyUnroutable);
            Assert.Equal(1, result.RecoveredCount);
        }

        await using (var db = database.CreateContext())
        {
            var stored = db.Integrations.Single(i => i.Id == integration.Id);
            Assert.Null(stored.UnroutableAlertedAt);
        }
    }
}
