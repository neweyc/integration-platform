using ControlPlane.Features.Messages;
using ControlPlane.Features.Triggers;
using Shared.Domain;

namespace ControlPlane.Tests.IntegrationTests;

public class PublishMessageHandlerIntegrationTests
{
    [Fact]
    public async Task Publish_FansOutToMatchingSubscribers_InSameEnvironment()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenant = new Tenant { Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" };

        // Two production subscribers to "high-wind" (should both receive), plus two that must NOT:
        // a different subject, and the right subject in a different environment.
        var subscriberA = Subscriber(tenant.Id, "wind-job-a", "production", subject: "high-wind");
        var subscriberB = Subscriber(tenant.Id, "wind-job-b", "production", subject: "high-wind");
        var otherSubject = Subscriber(tenant.Id, "calm-job", "production", subject: "low-wind");
        var otherEnv = Subscriber(tenant.Id, "wind-job-staging", "staging", subject: "high-wind");

        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(tenant);
            TestEnvironments.Seed(db, tenant.Id, "production", "staging");
            foreach (var (integration, trigger) in new[] { subscriberA, subscriberB, otherSubject, otherEnv })
            {
                db.Integrations.Add(integration);
                db.IntegrationTriggers.Add(trigger);
            }
            await db.SaveChangesAsync();
        }

        var sourceExecutionId = Guid.NewGuid();
        Guid messageId;

        await using (var db = database.CreateContext())
        {
            var handler = new PublishMessageHandler(db, new TriggerWorkItemProducer(db));
            var result = await handler.HandleAsync(new PublishMessageCommand(
                tenant.Id,
                "production",
                "high-wind",
                """{"observedAt":"2026-06-10T00:00:00Z"}""",
                sourceExecutionId));

            Assert.Equal(2, result.SubscriberCount);
            messageId = result.MessageId;
        }

        await using (var db = database.CreateContext())
        {
            // The envelope is persisted.
            var message = db.Messages.Single();
            Assert.Equal(messageId, message.Id);
            Assert.Equal("high-wind", message.Subject);
            Assert.Equal("production", message.Environment);
            Assert.Equal(sourceExecutionId, message.SourceExecutionId);

            // Exactly the two production "high-wind" subscribers got Queue work items.
            var workItems = db.WorkItems.ToList();
            Assert.Equal(2, workItems.Count);
            Assert.All(workItems, w =>
            {
                Assert.Equal(TriggerSource.Queue, w.TriggerSource);
                Assert.Equal("""{"observedAt":"2026-06-10T00:00:00Z"}""", w.Payload);
                Assert.Equal(messageId, w.MessageId);
                Assert.Equal(messageId.ToString(), w.DeliveryId);
                // Lineage: parent and (fallback) root both point at the publishing execution.
                Assert.Equal(sourceExecutionId, w.ParentExecutionId);
                Assert.Equal(sourceExecutionId, w.RootExecutionId);
            });

            var targeted = workItems.Select(w => w.IntegrationId).ToHashSet();
            Assert.Contains(subscriberA.Integration.Id, targeted);
            Assert.Contains(subscriberB.Integration.Id, targeted);
            Assert.DoesNotContain(otherSubject.Integration.Id, targeted);
            Assert.DoesNotContain(otherEnv.Integration.Id, targeted);
        }
    }

    [Fact]
    public async Task Publish_WithNoSubscribers_RecordsEnvelopeAndZeroFanOut()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        var tenant = new Tenant { Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" };

        await using (var db = database.CreateContext())
        {
            db.Tenants.Add(tenant);
            TestEnvironments.Seed(db, tenant.Id, "production");
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var handler = new PublishMessageHandler(db, new TriggerWorkItemProducer(db));
            var result = await handler.HandleAsync(new PublishMessageCommand(
                tenant.Id, "production", "nobody-listening", "{}", SourceExecutionId: null));

            Assert.Equal(0, result.SubscriberCount);
        }

        await using (var db = database.CreateContext())
        {
            // Publishing a fact nobody consumes is not an error — but it is recorded.
            Assert.Single(db.Messages);
            Assert.Empty(db.WorkItems);
        }
    }

    private static (Integration Integration, IntegrationTrigger Trigger) Subscriber(
        Guid tenantId, string slug, string environment, string subject)
    {
        var integration = new Integration
        {
            TenantId = tenantId,
            Name = slug,
            Slug = slug,
            Environment = environment,
            ClassName = $"Acme.{slug}",
            Status = IntegrationStatus.Enabled
        };
        var trigger = new IntegrationTrigger
        {
            TenantId = tenantId,
            IntegrationId = integration.Id,
            Name = "Message",
            Slug = "message",
            Type = TriggerType.Queue,
            Enabled = true,
            Subject = subject,
            DeclaredSubject = subject
        };
        return (integration, trigger);
    }
}
