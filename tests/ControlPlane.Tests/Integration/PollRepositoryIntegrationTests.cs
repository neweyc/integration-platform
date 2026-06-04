using ControlPlane.Features.AgentTokens;
using Shared.Domain;

namespace ControlPlane.Tests.IntegrationTests;

public class PollRepositoryIntegrationTests
{
    [Fact]
    public async Task ClaimPendingManualRunsAsync_ReclaimsExpiredClaimAndSkipsRunningIntegration()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenant = new Tenant { Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" };
        var reclaimableIntegration = CreateIntegration(tenant.Id, "reclaimable");
        var runningIntegration = CreateIntegration(tenant.Id, "running");
        var previousAgentId = Guid.NewGuid();
        var newAgentId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(tenant);
            db.Integrations.AddRange(reclaimableIntegration, runningIntegration);

            // A work item with an expired claim that can be reclaimed
            db.WorkItems.Add(new WorkItem
            {
                TenantId = tenant.Id,
                IntegrationId = reclaimableIntegration.Id,
                Environment = "production",
                TriggerSource = TriggerSource.Manual,
                Status = WorkItemStatus.Claimed,
                AvailableAt = now.AddMinutes(-10),
                ClaimOwner = previousAgentId,
                ClaimExpiresAt = now.AddMinutes(-5)
            });

            // A work item that is pending but the integration is running
            db.WorkItems.Add(new WorkItem
            {
                TenantId = tenant.Id,
                IntegrationId = runningIntegration.Id,
                Environment = "production",
                TriggerSource = TriggerSource.Manual,
                Status = WorkItemStatus.Pending,
                AvailableAt = now
            });

            db.ExecutionRecords.Add(new ExecutionRecord
            {
                TenantId = tenant.Id,
                IntegrationId = runningIntegration.Id,
                Environment = "production",
                Status = ExecutionStatus.Running,
                TriggerSource = TriggerSource.Manual
            });
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var repository = new PollRepository(db);
            var claimed = await repository.ClaimPendingManualRunsAsync(
                tenant.Id, "production", newAgentId, TimeSpan.FromMinutes(5), now, CancellationToken.None);

            Assert.Single(claimed);
            Assert.Equal(reclaimableIntegration.Id, claimed[0].Integration.Id);
            Assert.Equal(newAgentId, claimed[0].WorkItem.ClaimOwner);
            Assert.Equal(WorkItemStatus.Claimed, claimed[0].WorkItem.Status);
            Assert.Equal(now.AddMinutes(5), claimed[0].WorkItem.ClaimExpiresAt);
        }

        await using (var db = database.CreateContext())
        {
            var runningWorkItem = db.WorkItems.Single(w => w.IntegrationId == runningIntegration.Id);
            Assert.Equal(WorkItemStatus.Pending, runningWorkItem.Status);
            Assert.Null(runningWorkItem.ClaimOwner);
        }
    }

    [Fact]
    public async Task ClaimDueScheduledAsync_AcquiresWorkItemAndPersistsScheduleState()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenant = new Tenant { Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" };
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow.Date.AddHours(12);
        var integration = CreateIntegration(tenant.Id, "scheduled", now.AddMinutes(-1));

        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(tenant);
            db.Integrations.Add(integration);
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var repository = new PollRepository(db);
            var claimed = await repository.ClaimDueScheduledAsync(
                tenant.Id, "production", agentId, TimeSpan.FromMinutes(5), now, CancellationToken.None);

            Assert.Single(claimed);
            Assert.Equal(integration.Id, claimed[0].Integration.Id);
            Assert.Equal(WorkItemStatus.Claimed, claimed[0].WorkItem.Status);
            Assert.Equal(agentId, claimed[0].WorkItem.ClaimOwner);
            Assert.Equal(now.AddMinutes(5), claimed[0].WorkItem.ClaimExpiresAt);
        }

        await using (var db = database.CreateContext())
        {
            var state = db.IntegrationScheduleStates.Single(s => s.IntegrationId == integration.Id);
            Assert.NotNull(state.LastDispatchedAt);
            Assert.NotNull(state.NextRunAt);

            var workItem = db.WorkItems.Single(w => w.IntegrationId == integration.Id);
            Assert.Equal(WorkItemStatus.Claimed, workItem.Status);
            Assert.Equal(agentId, workItem.ClaimOwner);
        }
    }

    [Fact]
    public async Task ClaimPendingWebhookRunsAsync_ClaimsOnlyMatchingEnvironmentAndPreservesPayload()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenant = new Tenant { Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" };
        var productionIntegration = CreateIntegration(tenant.Id, "webhook-production");
        var productionTrigger = WebhookTrigger(tenant.Id, productionIntegration.Id, "production-webhook");
        productionIntegration.Triggers.Clear();
        productionIntegration.Triggers.Add(productionTrigger);

        var stagingIntegration = CreateIntegration(tenant.Id, "webhook-staging");
        stagingIntegration.Environment = "staging";
        var stagingTrigger = WebhookTrigger(tenant.Id, stagingIntegration.Id, "staging-webhook");
        stagingIntegration.Triggers.Clear();
        stagingIntegration.Triggers.Add(stagingTrigger);

        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(tenant);
            db.Integrations.AddRange(productionIntegration, stagingIntegration);
            db.WorkItems.AddRange(
                new WorkItem
                {
                    TenantId = tenant.Id,
                    IntegrationId = productionIntegration.Id,
                    IntegrationTriggerId = productionTrigger.Id,
                    Environment = "production",
                    TriggerSource = TriggerSource.Webhook,
                    Status = WorkItemStatus.Pending,
                    AvailableAt = now,
                    Payload = """{"env":"production"}"""
                },
                new WorkItem
                {
                    TenantId = tenant.Id,
                    IntegrationId = stagingIntegration.Id,
                    IntegrationTriggerId = stagingTrigger.Id,
                    Environment = "staging",
                    TriggerSource = TriggerSource.Webhook,
                    Status = WorkItemStatus.Pending,
                    AvailableAt = now,
                    Payload = """{"env":"staging"}"""
                });
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var repository = new PollRepository(db);
            var claimed = await repository.ClaimPendingWebhookRunsAsync(
                tenant.Id, "production", agentId, TimeSpan.FromMinutes(5), now, CancellationToken.None);

            var item = Assert.Single(claimed);
            Assert.Equal(productionIntegration.Id, item.Integration.Id);
            Assert.Equal(WorkItemStatus.Claimed, item.WorkItem.Status);
            Assert.Equal(agentId, item.WorkItem.ClaimOwner);
            Assert.Equal("""{"env":"production"}""", item.WorkItem.Payload);
        }

        await using (var db = database.CreateContext())
        {
            var stagingWorkItem = db.WorkItems.Single(w => w.IntegrationId == stagingIntegration.Id);
            Assert.Equal(WorkItemStatus.Pending, stagingWorkItem.Status);
            Assert.Null(stagingWorkItem.ClaimOwner);
        }
    }

    [Fact]
    public async Task ClaimDueScheduledAsync_SkipsIntegrationWithRunningExecution()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenant = new Tenant { Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" };
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow.Date.AddHours(12);
        var runningIntegration = CreateIntegration(tenant.Id, "running-job", now.AddMinutes(-1));
        var freeIntegration = CreateIntegration(tenant.Id, "free-job", now.AddMinutes(-1));

        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(tenant);
            db.Integrations.AddRange(runningIntegration, freeIntegration);
            db.ExecutionRecords.Add(new ExecutionRecord
            {
                TenantId = tenant.Id,
                IntegrationId = runningIntegration.Id,
                Environment = "production",
                Status = ExecutionStatus.Running,
                TriggerSource = TriggerSource.Scheduled
            });
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var repository = new PollRepository(db);
            var claimed = await repository.ClaimDueScheduledAsync(
                tenant.Id, "production", agentId, TimeSpan.FromMinutes(5), now, CancellationToken.None);

            Assert.Single(claimed);
            Assert.Equal(freeIntegration.Id, claimed[0].Integration.Id);
        }

        await using (var db = database.CreateContext())
        {
            Assert.False(db.WorkItems.Any(w => w.IntegrationId == runningIntegration.Id));
        }
    }

    [Fact]
    public async Task ClaimDueScheduledAsync_SkipsIntegrationWithActiveWorkItem()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenant = new Tenant { Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" };
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow.Date.AddHours(12);
        var integration = CreateIntegration(tenant.Id, "job", now.AddMinutes(-1));

        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(tenant);
            db.Integrations.Add(integration);
            // Pre-existing active work item (another agent already claimed this period)
            db.WorkItems.Add(new WorkItem
            {
                TenantId = tenant.Id,
                IntegrationId = integration.Id,
                IntegrationTriggerId = integration.Triggers.Single().Id,
                Environment = "production",
                TriggerSource = TriggerSource.Scheduled,
                Status = WorkItemStatus.Started,
                AvailableAt = now,
                ClaimOwner = Guid.NewGuid(),
                ClaimExpiresAt = now.AddMinutes(5)
            });
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var repository = new PollRepository(db);
            var claimed = await repository.ClaimDueScheduledAsync(
                tenant.Id, "production", agentId, TimeSpan.FromMinutes(5), now, CancellationToken.None);

            Assert.Empty(claimed);
        }
    }

    [Fact]
    public async Task ClaimDueScheduledAsync_ReclaimsExpiredScheduledWorkItem()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenant = new Tenant { Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" };
        var previousAgentId = Guid.NewGuid();
        var newAgentId = Guid.NewGuid();
        var now = DateTime.UtcNow.Date.AddHours(12);
        var integration = CreateIntegration(tenant.Id, "expired-claim-job", now.AddMinutes(-10));

        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(tenant);
            db.Integrations.Add(integration);
            db.WorkItems.Add(new WorkItem
            {
                TenantId = tenant.Id,
                IntegrationId = integration.Id,
                IntegrationTriggerId = integration.Triggers.Single().Id,
                Environment = "production",
                TriggerSource = TriggerSource.Scheduled,
                Status = WorkItemStatus.Claimed,
                AvailableAt = now.AddMinutes(-10),
                ClaimOwner = previousAgentId,
                ClaimExpiresAt = now.AddMinutes(-1)
            });
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var repository = new PollRepository(db);
            var claimed = await repository.ClaimDueScheduledAsync(
                tenant.Id, "production", newAgentId, TimeSpan.FromMinutes(5), now, CancellationToken.None);

            Assert.Single(claimed);
            Assert.Equal(integration.Id, claimed[0].Integration.Id);
            Assert.Equal(newAgentId, claimed[0].WorkItem.ClaimOwner);
            Assert.Equal(now.AddMinutes(5), claimed[0].WorkItem.ClaimExpiresAt);
        }

        await using (var db = database.CreateContext())
        {
            var workItem = db.WorkItems.Single(w => w.IntegrationId == integration.Id);

            Assert.Equal(WorkItemStatus.Claimed, workItem.Status);
            Assert.Equal(newAgentId, workItem.ClaimOwner);
            Assert.Equal(now.AddMinutes(5), workItem.ClaimExpiresAt);
        }
    }

    [Fact]
    public async Task ClaimDueScheduledAsync_ClaimsIntegrationOnceRunningExecutionCompletes()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenant = new Tenant { Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" };
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow.Date.AddHours(12);
        var integration = CreateIntegration(tenant.Id, "job", now.AddMinutes(-1));

        ExecutionRecord execution;

        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(tenant);
            db.Integrations.Add(integration);
            execution = new ExecutionRecord
            {
                TenantId = tenant.Id,
                IntegrationId = integration.Id,
                Environment = "production",
                Status = ExecutionStatus.Running,
                TriggerSource = TriggerSource.Scheduled
            };
            db.ExecutionRecords.Add(execution);
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var repository = new PollRepository(db);
            var claimed = await repository.ClaimDueScheduledAsync(
                tenant.Id, "production", agentId, TimeSpan.FromMinutes(5), now, CancellationToken.None);
            Assert.Empty(claimed);
        }

        await using (var db = database.CreateContext())
        {
            var record = db.ExecutionRecords.Single(e => e.Id == execution.Id);
            record.Status = ExecutionStatus.Succeeded;
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var repository = new PollRepository(db);
            var claimed = await repository.ClaimDueScheduledAsync(
                tenant.Id, "production", agentId, TimeSpan.FromMinutes(5), now, CancellationToken.None);
            Assert.Single(claimed);
            Assert.Equal(integration.Id, claimed[0].Integration.Id);
        }
    }

    private static Integration CreateIntegration(Guid tenantId, string slug, DateTime? createdAt = null) => new()
    {
        TenantId = tenantId,
        Name = slug,
        Slug = slug,
        Environment = "production",
        Status = IntegrationStatus.Enabled,
        ClassName = $"Tests.{slug}.Integration",
        CreatedAt = createdAt ?? DateTime.UtcNow,
        Triggers =
        [
            new IntegrationTrigger
            {
                TenantId = tenantId,
                Name = "Schedule",
                Slug = "schedule",
                Type = TriggerType.Scheduled,
                Enabled = true,
                CronExpression = "* * * * *",
                CreatedAt = createdAt ?? DateTime.UtcNow
            }
        ]
    };

    private static IntegrationTrigger WebhookTrigger(Guid tenantId, Guid integrationId, string slug) => new()
    {
        TenantId = tenantId,
        IntegrationId = integrationId,
        Name = slug,
        Slug = slug,
        Type = TriggerType.Webhook,
        Enabled = true,
        EncryptedWebhookSecret = "encrypted"
    };
}
