using ControlPlane.Features.Integrations;
using Shared.Domain;

namespace ControlPlane.Tests.IntegrationTests;

public class IntegrationRepositoryIntegrationTests
{
    [Fact]
    public async Task UpsertBySlugAsync_ExistingWebhookTrigger_PreservesWebhookSecret()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenant = new Tenant { Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" };
        var integration = new Integration
        {
            TenantId = tenant.Id,
            Name = "Order Sync",
            Slug = "order-sync",
            Environment = "production",
            ClassName = "Acme.OrderSync",
            Status = IntegrationStatus.Enabled,
            Triggers =
            [
                new IntegrationTrigger
                {
                    TenantId = tenant.Id,
                    Name = "Hook",
                    Slug = "hook",
                    Type = TriggerType.Webhook,
                    Enabled = true,
                    EncryptedWebhookSecret = "encrypted-existing-secret"
                }
            ]
        };

        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(tenant);
            db.Integrations.Add(integration);
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var repository = new IntegrationRepository(db);
            var result = await repository.UpsertBySlugAsync(new Integration
            {
                TenantId = tenant.Id,
                Name = "Order Sync Updated",
                Slug = "order-sync",
                Environment = "production",
                ClassName = "Acme.OrderSync",
                Status = IntegrationStatus.Enabled
            }, [
                new IntegrationTrigger
                {
                    TenantId = tenant.Id,
                    Name = "Hook",
                    Slug = "hook",
                    Type = TriggerType.Webhook,
                    Enabled = true,
                    EncryptedWebhookSecret = "encrypted-new-secret"
                }
            ]);

            Assert.False(result.Created);
            var triggerResult = Assert.Single(result.Triggers);
            Assert.False(triggerResult.Created);
            Assert.True(triggerResult.WebhookSecretPreserved);
        }

        await using (var db = database.CreateContext())
        {
            var trigger = db.IntegrationTriggers.Single(t => t.TenantId == tenant.Id && t.Slug == "hook");
            Assert.Equal("encrypted-existing-secret", trigger.EncryptedWebhookSecret);
        }
    }
}
