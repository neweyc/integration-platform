using System.Security.Cryptography;
using System.Text;
using ControlPlane.Features.Triggers;
using ControlPlane.Features.Webhooks;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Webhooks;

public class DeliverWebhookHandlerTests
{
    private readonly IWebhookRepository _repository = Substitute.For<IWebhookRepository>();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly ITriggerWorkItemProducer _workItemProducer = Substitute.For<ITriggerWorkItemProducer>();
    private readonly ITriggerEventRecorder _triggerEvents = Substitute.For<ITriggerEventRecorder>();
    private readonly DeliverWebhookHandler _handler;

    public DeliverWebhookHandlerTests()
    {
        _triggerEvents.RecordAsync(Arg.Any<TriggerEventRecord>())
            .Returns(call =>
            {
                var record = call.Arg<TriggerEventRecord>();
                return new TriggerEvent
                {
                    TenantId = record.TenantId,
                    IntegrationId = record.IntegrationId,
                    IntegrationTriggerId = record.IntegrationTriggerId,
                    AdapterKey = record.AdapterKey,
                    Source = record.Source,
                    EventKey = record.EventKey,
                    Outcome = record.Outcome,
                    WorkItemId = record.WorkItemId,
                    ErrorMessage = record.ErrorMessage,
                    ReceivedAt = record.ReceivedAt
                };
            });
        _handler = new DeliverWebhookHandler(_repository, _encryption, _workItemProducer, _triggerEvents);
    }

    [Fact]
    public async Task HandleAsync_ValidWebhook_QueuesWorkItem()
    {
        var (tenant, integration, trigger) = MakeWebhook();
        var body = Encoding.UTF8.GetBytes("""{"orderId":123}""");
        const string secret = "whs_secret";
        var ts = Now();

        _repository.FindAsync("acme", "orders", "default").Returns((tenant, integration, trigger));
        _encryption.Decrypt(trigger.EncryptedWebhookSecret!).Returns(secret);
        _workItemProducer.EnqueueAsync(Arg.Any<TriggerWorkItemRequest>())
            .Returns(call =>
            {
                var request = call.Arg<TriggerWorkItemRequest>();
                var workItem = new WorkItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = request.TenantId,
                    IntegrationId = request.IntegrationId,
                    IntegrationTriggerId = request.IntegrationTriggerId,
                    Environment = request.Environment,
                    TriggerSource = request.TriggerSource,
                    Status = WorkItemStatus.Pending,
                    AvailableAt = request.AvailableAt,
                    Payload = request.Payload,
                    DeliveryId = request.DeliveryId
                };
                return new TriggerWorkItemResult(TriggerWorkItemOutcome.ConvertedToWork, workItem);
            });

        var result = await _handler.HandleAsync(Command(Signature(secret, ts, body), ts, "delivery-1", body));

        Assert.True(result.Queued);
        Assert.NotEqual(Guid.Empty, result.WorkItemId);
        await _workItemProducer.Received(1).EnqueueAsync(Arg.Is<TriggerWorkItemRequest>(r =>
            r.TenantId == tenant.Id
            && r.IntegrationId == integration.Id
            && r.IntegrationTriggerId == trigger.Id
            && r.Environment == integration.Environment
            && r.TriggerSource == TriggerSource.Webhook
            && r.Payload == """{"orderId":123}"""
            && r.DeliveryId == "delivery-1"));
        await _repository.Received(1).RecordDeliveryAsync(Arg.Is<WebhookDelivery>(d =>
            d.TenantId == tenant.Id
            && d.IntegrationId == integration.Id
            && d.IntegrationTriggerId == trigger.Id
            && d.DeliveryId == "delivery-1"
            && d.Outcome == WebhookDeliveryOutcome.Accepted
            && d.WorkItemId == result.WorkItemId));
        await _triggerEvents.Received(1).RecordAsync(Arg.Is<TriggerEventRecord>(e =>
            e.Outcome == TriggerEventOutcome.Received
            && e.AdapterKey == "webhook"
            && e.EventKey == "delivery-1"
            && e.WorkItemId == null));
        await _triggerEvents.Received(1).RecordAsync(Arg.Is<TriggerEventRecord>(e =>
            e.Outcome == TriggerEventOutcome.Accepted
            && e.AdapterKey == "webhook"
            && e.EventKey == "delivery-1"
            && e.WorkItemId == result.WorkItemId));
    }

    [Fact]
    public async Task HandleAsync_InvalidSignature_ThrowsUnauthorized()
    {
        var (tenant, integration, trigger) = MakeWebhook();
        var body = Encoding.UTF8.GetBytes("{}");

        _repository.FindAsync("acme", "orders", "default").Returns((tenant, integration, trigger));
        _encryption.Decrypt(trigger.EncryptedWebhookSecret!).Returns("whs_secret");

        await Assert.ThrowsAsync<UnauthorizedException>(() => _handler.HandleAsync(
            Command("sha256=bad", Now(), null, body)));

        await _repository.Received(1).RecordDeliveryAsync(Arg.Is<WebhookDelivery>(d =>
            d.TenantId == tenant.Id
            && d.IntegrationId == integration.Id
            && d.IntegrationTriggerId == trigger.Id
            && d.Outcome == WebhookDeliveryOutcome.InvalidSignature));
        await _triggerEvents.Received(1).RecordAsync(Arg.Is<TriggerEventRecord>(e =>
            e.Outcome == TriggerEventOutcome.Rejected
            && e.ErrorMessage == "Invalid webhook signature."));
    }

    [Fact]
    public async Task HandleAsync_StaleTimestamp_ThrowsUnauthorizedAndRecordsExpired()
    {
        var (tenant, integration, trigger) = MakeWebhook();
        var body = Encoding.UTF8.GetBytes("{}");
        const string secret = "whs_secret";
        var staleTs = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600).ToString();

        _repository.FindAsync("acme", "orders", "default").Returns((tenant, integration, trigger));
        _encryption.Decrypt(trigger.EncryptedWebhookSecret!).Returns(secret);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _handler.HandleAsync(
            Command(Signature(secret, staleTs, body), staleTs, "d1", body)));

        await _repository.Received(1).RecordDeliveryAsync(Arg.Is<WebhookDelivery>(d =>
            d.TenantId == tenant.Id
            && d.IntegrationId == integration.Id
            && d.IntegrationTriggerId == trigger.Id
            && d.Outcome == WebhookDeliveryOutcome.Expired));
        await _triggerEvents.Received(1).RecordAsync(Arg.Is<TriggerEventRecord>(e =>
            e.Outcome == TriggerEventOutcome.Rejected
            && e.EventKey == "d1"
            && e.ErrorMessage == "Webhook timestamp is missing or outside the allowed window."));
        await _workItemProducer.DidNotReceive().EnqueueAsync(Arg.Any<TriggerWorkItemRequest>());
    }

    [Fact]
    public async Task HandleAsync_MissingTimestamp_IsRejectedAsExpired()
    {
        var webhook = MakeWebhook();
        var body = Encoding.UTF8.GetBytes("{}");
        const string secret = "whs_secret";

        _repository.FindAsync("acme", "orders", "default").Returns(webhook);
        _encryption.Decrypt(webhook.Trigger.EncryptedWebhookSecret!).Returns(secret);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _handler.HandleAsync(
            Command(Signature(secret, "", body), null, "d1", body)));

        await _repository.Received(1).RecordDeliveryAsync(Arg.Is<WebhookDelivery>(d =>
            d.Outcome == WebhookDeliveryOutcome.Expired));
    }

    [Fact]
    public async Task HandleAsync_DisabledIntegration_ThrowsNotFound()
    {
        var webhook = MakeWebhook();
        webhook.Integration.Status = IntegrationStatus.Disabled;

        _repository.FindAsync("acme", "orders", "default").Returns(webhook);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(
            Command("sha256=anything", Now(), null, [])));
    }

    [Fact]
    public async Task HandleAsync_DuplicateDeliveryId_DoesNotQueueAgain()
    {
        var (tenant, integration, trigger) = MakeWebhook();
        var body = Encoding.UTF8.GetBytes("{}");
        const string secret = "whs_secret";
        var ts = Now();

        _repository.FindAsync("acme", "orders", "default").Returns((tenant, integration, trigger));
        _encryption.Decrypt(trigger.EncryptedWebhookSecret!).Returns(secret);
        _repository.DeliveryExistsAsync(tenant.Id, integration.Id, trigger.Id, "delivery-1").Returns(true);

        var result = await _handler.HandleAsync(Command(Signature(secret, ts, body), ts, "delivery-1", body));

        Assert.False(result.Queued);
        Assert.Equal(Guid.Empty, result.WorkItemId);
        await _workItemProducer.DidNotReceive().EnqueueAsync(Arg.Any<TriggerWorkItemRequest>());
        await _repository.Received(1).RecordDeliveryAsync(Arg.Is<WebhookDelivery>(d =>
            d.TenantId == tenant.Id
            && d.IntegrationId == integration.Id
            && d.IntegrationTriggerId == trigger.Id
            && d.DeliveryId == "delivery-1"
            && d.Outcome == WebhookDeliveryOutcome.Deduplicated));
        await _triggerEvents.Received(1).RecordAsync(Arg.Is<TriggerEventRecord>(e =>
            e.Outcome == TriggerEventOutcome.Deduplicated
            && e.EventKey == "delivery-1"));
    }

    [Fact]
    public async Task HandleAsync_ConcurrentDuplicateLosesUniqueRace_DoesNotQueue()
    {
        var (tenant, integration, trigger) = MakeWebhook();
        var body = Encoding.UTF8.GetBytes("{}");
        const string secret = "whs_secret";
        var ts = Now();

        _repository.FindAsync("acme", "orders", "default").Returns((tenant, integration, trigger));
        _encryption.Decrypt(trigger.EncryptedWebhookSecret!).Returns(secret);
        _repository.DeliveryExistsAsync(tenant.Id, integration.Id, trigger.Id, "delivery-1").Returns(false);
        _workItemProducer.EnqueueAsync(Arg.Any<TriggerWorkItemRequest>())
            .Returns(new TriggerWorkItemResult(TriggerWorkItemOutcome.Deduplicated, null));

        var result = await _handler.HandleAsync(Command(Signature(secret, ts, body), ts, "delivery-1", body));

        Assert.False(result.Queued);
        Assert.Equal(Guid.Empty, result.WorkItemId);
        await _repository.Received(1).RecordDeliveryAsync(Arg.Is<WebhookDelivery>(d =>
            d.TenantId == tenant.Id
            && d.IntegrationId == integration.Id
            && d.IntegrationTriggerId == trigger.Id
            && d.DeliveryId == "delivery-1"
            && d.Outcome == WebhookDeliveryOutcome.Deduplicated));
    }

    [Fact]
    public async Task HandleAsync_UnknownIntegration_ThrowsNotFound()
    {
        _repository.FindAsync("acme", "ghost", "default").Returns(((Tenant, Integration, IntegrationTrigger)?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(
            new DeliverWebhookCommand("acme", "ghost", "default", "sha256=x", Now(), null, [])));
    }

    [Fact]
    public async Task HandleAsync_OversizedPayload_ThrowsValidation()
    {
        var body = new byte[DeliverWebhookHandler.MaxPayloadBytes + 1];

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(
            Command("sha256=x", Now(), null, body)));
    }

    private static DeliverWebhookCommand Command(string? signature, string? timestamp, string? deliveryId, byte[] body) =>
        new("acme", "orders", "default", signature, timestamp, deliveryId, body);

    private static (Tenant Tenant, Integration Integration, IntegrationTrigger Trigger) MakeWebhook()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "acme" };
        var integration = new Integration
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Orders",
            Slug = "orders",
            Environment = "production",
            Status = IntegrationStatus.Enabled,
            ClassName = "Acme.Orders"
        };
        var trigger = new IntegrationTrigger
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            IntegrationId = integration.Id,
            Name = "Default",
            Slug = "default",
            Type = TriggerType.Webhook,
            Enabled = true,
            EncryptedWebhookSecret = "encrypted"
        };
        return (tenant, integration, trigger);
    }

    private static string Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

    private static string Signature(string secret, string timestamp, byte[] body)
    {
        var payload = Encoding.UTF8.GetBytes($"{timestamp}.").Concat(body).ToArray();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
