using ControlPlane.Features.Integrations;
using Shared.Domain;

namespace ControlPlane.Tests.IntegrationTests;

public class UnroutableIntegrationsIntegrationTests
{
    [Fact]
    public async Task ListsTagGatedIntegrationsNoLiveAgentCovers()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenant = new Tenant { Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" };
        var uncovered = Gated(tenant.Id, "reactor", "hardware-signal");
        var covered = Gated(tenant.Id, "render", "gpu");
        var untagged = new Integration
        {
            TenantId = tenant.Id, Name = "Plain", Slug = "plain", Environment = "production",
            ClassName = "A.Plain", Status = IntegrationStatus.Enabled
        };
        var liveToken = new AgentToken { TenantId = tenant.Id, Name = "live", Environment = "production", TokenHash = Guid.NewGuid().ToString("N") };
        var staleToken = new AgentToken { TenantId = tenant.Id, Name = "stale", Environment = "production", TokenHash = Guid.NewGuid().ToString("N") };

        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(tenant);
            TestEnvironments.Seed(db, tenant.Id, "production");
            db.Integrations.AddRange(uncovered, covered, untagged);
            db.AgentTokens.AddRange(liveToken, staleToken);

            // Live agent offers "gpu" (covers render) but not "hardware-signal".
            db.AgentHeartbeats.Add(new AgentHeartbeat
            {
                TenantId = tenant.Id, AgentTokenId = liveToken.Id, Environment = "production",
                Tags = ["gpu"], LastSeenAt = DateTime.UtcNow
            });
            // A stale agent offers "hardware-signal" but is too old to count as live.
            db.AgentHeartbeats.Add(new AgentHeartbeat
            {
                TenantId = tenant.Id, AgentTokenId = staleToken.Id, Environment = "production",
                Tags = ["hardware-signal"], LastSeenAt = DateTime.UtcNow.AddMinutes(-30)
            });
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var handler = new GetUnroutableIntegrationsHandler(new RoutingHealthRepository(db));
            var result = await handler.HandleAsync(new GetUnroutableIntegrationsCommand(tenant.Id));

            // reactor: only the stale agent offers its tag → unroutable.
            // render: covered by the live agent. plain: no required tags. Neither is listed.
            var item = Assert.Single(result.Integrations);
            Assert.Equal("reactor", item.Slug);
            Assert.Equal(["hardware-signal"], item.RequiredTags);
        }
    }

    private static Integration Gated(Guid tenantId, string slug, params string[] tags) => new()
    {
        TenantId = tenantId,
        Name = slug,
        Slug = slug,
        Environment = "production",
        ClassName = $"A.{slug}",
        Status = IntegrationStatus.Enabled,
        RequiredTags = tags
    };
}
