using ControlPlane.Features.AgentTokens;
using ControlPlane.Features.Triggers;
using Shared.Domain;

namespace ControlPlane.Tests.IntegrationTests;

public class TriggerWorkItemProducerIntegrationTests
{
    [Fact]
    public async Task QueueAdapter_CanUseSharedProducerPath()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenant = new Tenant { Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" };
        var integration = new Integration
        {
            TenantId = tenant.Id,
            Name = "Queue Sync",
            Slug = "queue-sync",
            Environment = "production",
            ClassName = "Acme.QueueSync",
            Status = IntegrationStatus.Enabled
        };
        var trigger = new IntegrationTrigger
        {
            TenantId = tenant.Id,
            IntegrationId = integration.Id,
            Name = "Orders Queue",
            Slug = "orders",
            Type = TriggerType.Queue,
            Enabled = true
        };
        var now = DateTime.UtcNow;

        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(tenant);
            db.Integrations.Add(integration);
            db.IntegrationTriggers.Add(trigger);
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var adapter = new TestQueueAdapter(new TriggerWorkItemProducer(db));
            var result = await adapter.ReceiveAsync(
                tenant.Id,
                integration.Id,
                trigger.Id,
                "production",
                """{"messageId":"m-1"}""",
                "m-1",
                now);

            Assert.Equal(TriggerWorkItemOutcome.ConvertedToWork, result.Outcome);
            Assert.NotNull(result.WorkItem);
        }

        await using (var db = database.CreateContext())
        {
            var workItem = db.WorkItems.Single();
            Assert.Equal(tenant.Id, workItem.TenantId);
            Assert.Equal(integration.Id, workItem.IntegrationId);
            Assert.Equal(trigger.Id, workItem.IntegrationTriggerId);
            Assert.Equal("production", workItem.Environment);
            Assert.Equal(TriggerSource.Queue, workItem.TriggerSource);
            Assert.Equal(WorkItemStatus.Pending, workItem.Status);
            Assert.Equal("""{"messageId":"m-1"}""", workItem.Payload);
            Assert.Equal("m-1", workItem.DeliveryId);
            Assert.Equal(now, workItem.AvailableAt);
        }

        var agentId = Guid.NewGuid();

        await using (var db = database.CreateContext())
        {
            var repository = new PollRepository(db);
            var claimed = await repository.ClaimPendingQueueRunsAsync(
                tenant.Id,
                "production",
                agentId,
                TimeSpan.FromMinutes(5),
                now.AddSeconds(1));

            var item = Assert.Single(claimed);
            Assert.Equal(integration.Id, item.Integration.Id);
            Assert.Equal(TriggerSource.Queue, item.WorkItem.TriggerSource);
            Assert.Equal("""{"messageId":"m-1"}""", item.WorkItem.Payload);
            Assert.Equal(agentId, item.WorkItem.ClaimOwner);
        }

        await using (var db = database.CreateContext())
        {
            var workItem = db.WorkItems.Single();
            Assert.Equal(WorkItemStatus.Claimed, workItem.Status);
            Assert.Equal(agentId, workItem.ClaimOwner);
            Assert.NotNull(workItem.ClaimExpiresAt);
        }
    }

    private sealed class TestQueueAdapter(ITriggerWorkItemProducer producer)
    {
        public Task<TriggerWorkItemResult> ReceiveAsync(
            Guid tenantId,
            Guid integrationId,
            Guid triggerId,
            string environment,
            string payload,
            string deliveryId,
            DateTime receivedAt) =>
            producer.EnqueueAsync(
                new TriggerWorkItemRequest(
                    tenantId,
                    integrationId,
                    environment,
                    TriggerSource.Queue,
                    receivedAt,
                    IntegrationTriggerId: triggerId,
                    Payload: payload,
                    DeliveryId: deliveryId));
    }
}
