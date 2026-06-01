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
        var integration = CreateIntegration(tenant.Id, "scheduled");
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow.Date.AddHours(12);

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

    private static Integration CreateIntegration(Guid tenantId, string slug) => new()
    {
        TenantId = tenantId,
        Name = slug,
        Slug = slug,
        Environment = "production",
        Status = IntegrationStatus.Enabled,
        TriggerType = TriggerType.Scheduled,
        CronExpression = "* * * * *",
        ClassName = $"Tests.{slug}.Integration"
    };
}
