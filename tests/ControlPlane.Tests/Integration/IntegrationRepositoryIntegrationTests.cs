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
            TestEnvironments.Seed(db, tenant.Id, "production", "staging");
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

    [Fact]
    public async Task UpsertBySlugAsync_PreservesOperatorDisabledTrigger_AcrossRedeploy()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        // Operator disabled the trigger: active Enabled=false, but the code default (declared) is true.
        var tenant = await SeedScheduledAsync(database, enabled: false, cron: "0 0 * * *", declaredEnabled: true, declaredCron: "0 0 * * *");

        await using (var db = database.CreateContext())
        {
            var repository = new IntegrationRepository(db);
            var result = await repository.UpsertBySlugAsync(
                CodeIntegration(tenant.Id),
                [CodeScheduled(tenant.Id, "0 0 * * *", enabled: true)]);

            var trigger = Assert.Single(result.Triggers);
            Assert.True(trigger.EnabledOverridden);
            Assert.False(trigger.Trigger.Enabled);
        }

        await using (var db = database.CreateContext())
        {
            var trigger = db.IntegrationTriggers.Single(t => t.TenantId == tenant.Id && t.Slug == "schedule");
            Assert.False(trigger.Enabled);        // operator's disable survived the redeploy
            Assert.True(trigger.DeclaredEnabled);  // code default recorded
        }
    }

    [Fact]
    public async Task UpsertBySlugAsync_PreservesOperatorCronOverride_AndRecordsNewDeclaredDefault()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        // Operator overrode the cron: active "*/5 * * * *", declared "0 0 * * *".
        var tenant = await SeedScheduledAsync(database, enabled: true, cron: "*/5 * * * *", declaredEnabled: true, declaredCron: "0 0 * * *");

        await using (var db = database.CreateContext())
        {
            var repository = new IntegrationRepository(db);
            var result = await repository.UpsertBySlugAsync(
                CodeIntegration(tenant.Id),
                [CodeScheduled(tenant.Id, "30 1 * * *")]); // code now declares a different cron

            Assert.True(Assert.Single(result.Triggers).CronOverridden);
        }

        await using (var db = database.CreateContext())
        {
            var trigger = db.IntegrationTriggers.Single(t => t.TenantId == tenant.Id && t.Slug == "schedule");
            Assert.Equal("*/5 * * * *", trigger.CronExpression);        // operator override preserved
            Assert.Equal("30 1 * * *", trigger.DeclaredCronExpression);  // new code default recorded
        }
    }

    [Fact]
    public async Task UpsertBySlugAsync_NoOverride_FollowsCode()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenant = await SeedScheduledAsync(database, enabled: true, cron: "0 0 * * *", declaredEnabled: true, declaredCron: "0 0 * * *");

        await using (var db = database.CreateContext())
        {
            var repository = new IntegrationRepository(db);
            var result = await repository.UpsertBySlugAsync(
                CodeIntegration(tenant.Id),
                [CodeScheduled(tenant.Id, "30 2 * * *")]);

            var trigger = Assert.Single(result.Triggers);
            Assert.False(trigger.CronOverridden);
            Assert.False(trigger.EnabledOverridden);
        }

        await using (var db = database.CreateContext())
        {
            var trigger = db.IntegrationTriggers.Single(t => t.TenantId == tenant.Id && t.Slug == "schedule");
            Assert.Equal("30 2 * * *", trigger.CronExpression);        // followed the new code cron
            Assert.Equal("30 2 * * *", trigger.DeclaredCronExpression);
        }
    }

    [Fact]
    public async Task UpdateAsync_OperatorCronChange_BecomesOverridePreservedOnNextRedeploy()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenant = await SeedScheduledAsync(database, enabled: true, cron: "0 0 * * *", declaredEnabled: true, declaredCron: "0 0 * * *");
        var integrationId = await IntegrationIdAsync(database, tenant.Id);

        // Operator changes the cron through the operator update path.
        await using (var db = database.CreateContext())
        {
            var repository = new IntegrationRepository(db);
            var integration = await repository.GetByIdAsync(tenant.Id, integrationId);
            await repository.UpdateAsync(integration!, [CodeScheduled(tenant.Id, "15 9 * * *")]);
        }

        await using (var db = database.CreateContext())
        {
            var trigger = db.IntegrationTriggers.Single(t => t.TenantId == tenant.Id && t.Slug == "schedule");
            Assert.Equal("15 9 * * *", trigger.CronExpression);        // operator's new active value
            Assert.Equal("0 0 * * *", trigger.DeclaredCronExpression);  // declared default untouched by operator
        }

        // A subsequent code redeploy declaring the original cron must NOT clobber the operator override.
        await using (var db = database.CreateContext())
        {
            var repository = new IntegrationRepository(db);
            var result = await repository.UpsertBySlugAsync(
                CodeIntegration(tenant.Id),
                [CodeScheduled(tenant.Id, "0 0 * * *")]);

            Assert.True(Assert.Single(result.Triggers).CronOverridden);
        }

        await using (var db = database.CreateContext())
        {
            var trigger = db.IntegrationTriggers.Single(t => t.TenantId == tenant.Id && t.Slug == "schedule");
            Assert.Equal("15 9 * * *", trigger.CronExpression); // still the operator override
        }
    }

    private static async Task<Tenant> SeedScheduledAsync(
        IntegrationTestDatabase database,
        bool enabled,
        string? cron,
        bool declaredEnabled,
        string? declaredCron)
    {
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
                    Name = "Schedule",
                    Slug = "schedule",
                    Type = TriggerType.Scheduled,
                    Enabled = enabled,
                    CronExpression = cron,
                    DeclaredEnabled = declaredEnabled,
                    DeclaredCronExpression = declaredCron
                }
            ]
        };

        await using var db = database.CreateContext();
        db.Tenants.Add(tenant);
            TestEnvironments.Seed(db, tenant.Id, "production", "staging");
        db.Integrations.Add(integration);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static async Task<Guid> IntegrationIdAsync(IntegrationTestDatabase database, Guid tenantId)
    {
        await using var db = database.CreateContext();
        return db.Integrations.Single(i => i.TenantId == tenantId && i.Slug == "order-sync").Id;
    }

    [Fact]
    public async Task UpsertBySlugAsync_PreservesOperatorRequiredTagOverride_AndReportsDrift()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenantId = Guid.NewGuid();
        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" });
            TestEnvironments.Seed(db, tenantId, "production");
            await db.SaveChangesAsync();
        }

        // 1. Code provisions the integration with two required tags.
        await using (var db = database.CreateContext())
        {
            var repo = new IntegrationRepository(db);
            var code = CodeIntegration(tenantId);
            code.RequiredTags = ["hardware-signal", "gpu"];
            code.DeclaredRequiredTags = ["hardware-signal", "gpu"];
            var created = await repo.UpsertBySlugAsync(code, []);
            Assert.True(created.Created);
            Assert.False(created.RequiredTagsOverridden);
        }

        // text[] round-trips through Postgres.
        await using (var db = database.CreateContext())
        {
            var stored = db.Integrations.Single(i => i.TenantId == tenantId && i.Slug == "order-sync");
            Assert.True(TagSet.Equal(["hardware-signal", "gpu"], stored.RequiredTags));
        }

        // 2. An operator narrows the active tags (the Phase-4 edit path, simulated directly here).
        await using (var db = database.CreateContext())
        {
            var stored = db.Integrations.Single(i => i.TenantId == tenantId && i.Slug == "order-sync");
            stored.RequiredTags = ["hardware-signal"];
            await db.SaveChangesAsync();
        }

        // 3. A code redeploy must preserve the override and report drift.
        await using (var db = database.CreateContext())
        {
            var repo = new IntegrationRepository(db);
            var code = CodeIntegration(tenantId);
            code.RequiredTags = ["hardware-signal", "gpu"];
            code.DeclaredRequiredTags = ["hardware-signal", "gpu"];
            var result = await repo.UpsertBySlugAsync(code, []);
            Assert.False(result.Created);
            Assert.True(result.RequiredTagsOverridden);
        }

        await using (var db = database.CreateContext())
        {
            var stored = db.Integrations.Single(i => i.TenantId == tenantId && i.Slug == "order-sync");
            Assert.True(TagSet.Equal(["hardware-signal"], stored.RequiredTags));            // override preserved
            Assert.True(TagSet.Equal(["hardware-signal", "gpu"], stored.DeclaredRequiredTags)); // declared follows code
        }
    }

    [Fact]
    public async Task UpdateAsync_OperatorEditDroppingSubject_PreservesQueueSubscription()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenant = new Tenant { Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" };
        var integration = new Integration
        {
            TenantId = tenant.Id,
            Name = "Wind Job",
            Slug = "wind-job",
            Environment = "production",
            ClassName = "Acme.WindJob",
            Status = IntegrationStatus.Enabled,
            Triggers =
            [
                new IntegrationTrigger
                {
                    TenantId = tenant.Id,
                    Name = "Message",
                    Slug = "message",
                    Type = TriggerType.Queue,
                    Enabled = true,
                    Subject = "high-wind",
                    DeclaredSubject = "high-wind"
                }
            ]
        };

        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(tenant);
            TestEnvironments.Seed(db, tenant.Id, "production");
            db.Integrations.Add(integration);
            await db.SaveChangesAsync();
        }

        // The operator edit UI round-trips the Queue trigger but supplies no subject (the bug repro).
        await using (var db = database.CreateContext())
        {
            var repository = new IntegrationRepository(db);
            var existing = await repository.GetByIdAsync(tenant.Id, integration.Id);
            Assert.NotNull(existing);

            await repository.UpdateAsync(existing!,
            [
                new IntegrationTrigger
                {
                    TenantId = tenant.Id,
                    Name = "Message",
                    Slug = "message",
                    Type = TriggerType.Queue,
                    Enabled = true,
                    Subject = null,
                    DeclaredSubject = null
                }
            ]);
        }

        await using (var db = database.CreateContext())
        {
            var trigger = db.IntegrationTriggers.Single(t => t.TenantId == tenant.Id && t.Slug == "message");
            Assert.Equal("high-wind", trigger.Subject); // subscription survived the edit
        }
    }

    [Fact]
    public async Task UpdateAsync_OperatorSuppliesNewSubject_AppliesIt()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenant = new Tenant { Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" };
        var integration = new Integration
        {
            TenantId = tenant.Id,
            Name = "Wind Job",
            Slug = "wind-job",
            Environment = "production",
            ClassName = "Acme.WindJob",
            Status = IntegrationStatus.Enabled,
            Triggers =
            [
                new IntegrationTrigger
                {
                    TenantId = tenant.Id,
                    Name = "Message",
                    Slug = "message",
                    Type = TriggerType.Queue,
                    Enabled = true,
                    Subject = "high-wind",
                    DeclaredSubject = "high-wind"
                }
            ]
        };

        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(tenant);
            TestEnvironments.Seed(db, tenant.Id, "production");
            db.Integrations.Add(integration);
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var repository = new IntegrationRepository(db);
            var existing = await repository.GetByIdAsync(tenant.Id, integration.Id);

            await repository.UpdateAsync(existing!,
            [
                new IntegrationTrigger
                {
                    TenantId = tenant.Id,
                    Name = "Message",
                    Slug = "message",
                    Type = TriggerType.Queue,
                    Enabled = true,
                    Subject = "gale-warning",
                    DeclaredSubject = "gale-warning"
                }
            ]);
        }

        await using (var db = database.CreateContext())
        {
            var trigger = db.IntegrationTriggers.Single(t => t.TenantId == tenant.Id && t.Slug == "message");
            Assert.Equal("gale-warning", trigger.Subject); // an explicit subject is still applied
        }
    }

    private static Integration CodeIntegration(Guid tenantId) =>
        new()
        {
            TenantId = tenantId,
            Name = "Order Sync",
            Slug = "order-sync",
            Environment = "production",
            ClassName = "Acme.OrderSync",
            Status = IntegrationStatus.Enabled
        };

    // Mimics a trigger the package scan declares: declared defaults equal the active values.
    private static IntegrationTrigger CodeScheduled(Guid tenantId, string cron, bool enabled = true) =>
        new()
        {
            TenantId = tenantId,
            Name = "Schedule",
            Slug = "schedule",
            Type = TriggerType.Scheduled,
            Enabled = enabled,
            CronExpression = cron,
            DeclaredCronExpression = cron,
            DeclaredEnabled = enabled
        };
}
