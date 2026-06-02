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
            db.ManualRunRequests.AddRange(
                new ManualRunRequest
                {
                    TenantId = tenant.Id,
                    IntegrationId = reclaimableIntegration.Id,
                    Environment = "production",
                    Status = ManualRunStatus.Claimed,
                    ClaimedByAgentTokenId = previousAgentId,
                    ClaimedAt = now.AddMinutes(-10),
                    ClaimExpiresAt = now.AddMinutes(-5)
                },
                new ManualRunRequest
                {
                    TenantId = tenant.Id,
                    IntegrationId = runningIntegration.Id,
                    Environment = "production",
                    Status = ManualRunStatus.Pending
                });
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
            var claimed = await repository.ClaimPendingManualRunsAsync(
                tenant.Id,
                "production",
                newAgentId,
                TimeSpan.FromMinutes(5),
                now,
                CancellationToken.None);

            Assert.Single(claimed);
            Assert.Equal(reclaimableIntegration.Id, claimed[0].Request.IntegrationId);
            Assert.Equal(newAgentId, claimed[0].Request.ClaimedByAgentTokenId);
            Assert.Equal(ManualRunStatus.Claimed, claimed[0].Request.Status);
            Assert.Equal(now.AddMinutes(5), claimed[0].Request.ClaimExpiresAt);
        }

        await using (var db = database.CreateContext())
        {
            var runningRequest = db.ManualRunRequests.Single(r => r.IntegrationId == runningIntegration.Id);

            Assert.Equal(ManualRunStatus.Pending, runningRequest.Status);
            Assert.Null(runningRequest.ClaimedByAgentTokenId);
        }
    }

    [Fact]
    public async Task ClaimDueScheduledAsync_AcquiresLeaseAndPersistsScheduleState()
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
                tenant.Id,
                "production",
                agentId,
                TimeSpan.FromMinutes(5),
                now,
                CancellationToken.None);

            Assert.Single(claimed);
            Assert.Equal(integration.Id, claimed[0].Integration.Id);
            Assert.Equal(now.AddMinutes(5), claimed[0].LeaseExpiresAt);
        }

        await using (var db = database.CreateContext())
        {
            var state = db.IntegrationScheduleStates.Single(s => s.IntegrationId == integration.Id);

            Assert.Equal(agentId, state.LeaseOwnerId);
            Assert.Equal(now.AddMinutes(5), state.LeaseExpiresAt);
            Assert.NotNull(state.LastDispatchedAt);
            Assert.NotNull(state.NextRunAt);
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

        // Both integrations are due
        var runningIntegration = CreateIntegration(tenant.Id, "running-job", now.AddMinutes(-1));
        var freeIntegration = CreateIntegration(tenant.Id, "free-job", now.AddMinutes(-1));

        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(tenant);
            db.Integrations.AddRange(runningIntegration, freeIntegration);
            // Mark one integration as already running
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

            // Only the free integration should be claimed
            Assert.Single(claimed);
            Assert.Equal(freeIntegration.Id, claimed[0].Integration.Id);
        }

        await using (var db = database.CreateContext())
        {
            // Running integration's schedule state should not have been advanced
            var state = db.IntegrationScheduleStates.SingleOrDefault(s => s.IntegrationId == runningIntegration.Id);
            Assert.Null(state);
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

        // Poll while running — should not be claimed
        await using (var db = database.CreateContext())
        {
            var repository = new PollRepository(db);
            var claimed = await repository.ClaimDueScheduledAsync(
                tenant.Id, "production", agentId, TimeSpan.FromMinutes(5), now, CancellationToken.None);

            Assert.Empty(claimed);
        }

        // Complete the execution
        await using (var db = database.CreateContext())
        {
            var record = db.ExecutionRecords.Single(e => e.Id == execution.Id);
            record.Status = ExecutionStatus.Succeeded;
            await db.SaveChangesAsync();
        }

        // Poll again — now it should be claimed
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
        TriggerType = TriggerType.Scheduled,
        CronExpression = "* * * * *",
        ClassName = $"Tests.{slug}.Integration",
        CreatedAt = createdAt ?? DateTime.UtcNow
    };
}
