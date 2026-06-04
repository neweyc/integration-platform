using System.Security.Cryptography;
using System.Text;
using ControlPlane.Features.Webhooks;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Webhooks;

public class DeliverWebhookHandlerTests
{
    private readonly IWebhookRepository _repository = Substitute.For<IWebhookRepository>();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly DeliverWebhookHandler _handler;

    public DeliverWebhookHandlerTests()
    {
        _handler = new DeliverWebhookHandler(_repository, _encryption);
    }

    [Fact]
    public async Task HandleAsync_ValidWebhook_QueuesWorkItem()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "acme" };
        var integration = MakeWebhookIntegration(tenant.Id);
        var body = Encoding.UTF8.GetBytes("""{"orderId":123}""");
        const string secret = "whs_secret";
        var ts = Now();

        _repository.FindAsync("acme", "orders").Returns((tenant, integration));
        _encryption.Decrypt(integration.EncryptedWebhookSecret!).Returns(secret);
        _repository.CreateWorkItemAsync(Arg.Any<WorkItem>())
            .Returns(call =>
            {
                var item = call.Arg<WorkItem>();
                return new WorkItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = item.TenantId,
                    IntegrationId = item.IntegrationId,
                    Environment = item.Environment,
                    TriggerSource = item.TriggerSource,
                    Status = item.Status,
                    AvailableAt = item.AvailableAt,
                    Payload = item.Payload,
                    DeliveryId = item.DeliveryId
                };
            });

        var result = await _handler.HandleAsync(new DeliverWebhookCommand(
            "acme",
            "orders",
            Signature(secret, ts, body),
            ts,
            "delivery-1",
            body));

        Assert.True(result.Queued);
        Assert.NotEqual(Guid.Empty, result.WorkItemId);
        await _repository.Received(1).CreateWorkItemAsync(Arg.Is<WorkItem>(w =>
            w.TenantId == tenant.Id
            && w.IntegrationId == integration.Id
            && w.Environment == integration.Environment
            && w.TriggerSource == TriggerSource.Webhook
            && w.Status == WorkItemStatus.Pending
            && w.Payload == """{"orderId":123}"""
            && w.DeliveryId == "delivery-1"));
        await _repository.Received(1).RecordDeliveryAsync(Arg.Is<WebhookDelivery>(d =>
            d.TenantId == tenant.Id
            && d.IntegrationId == integration.Id
            && d.DeliveryId == "delivery-1"
            && d.Outcome == WebhookDeliveryOutcome.Accepted
            && d.WorkItemId == result.WorkItemId));
    }

    [Fact]
    public async Task HandleAsync_InvalidSignature_ThrowsUnauthorized()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "acme" };
        var integration = MakeWebhookIntegration(tenant.Id);
        var body = Encoding.UTF8.GetBytes("{}");

        _repository.FindAsync("acme", "orders").Returns((tenant, integration));
        _encryption.Decrypt(integration.EncryptedWebhookSecret!).Returns("whs_secret");

        await Assert.ThrowsAsync<UnauthorizedException>(() => _handler.HandleAsync(
            new DeliverWebhookCommand("acme", "orders", "sha256=bad", Now(), null, body)));

        await _repository.Received(1).RecordDeliveryAsync(Arg.Is<WebhookDelivery>(d =>
            d.TenantId == tenant.Id
            && d.IntegrationId == integration.Id
            && d.Outcome == WebhookDeliveryOutcome.InvalidSignature));
    }

    [Fact]
    public async Task HandleAsync_StaleTimestamp_ThrowsUnauthorizedAndRecordsExpired()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "acme" };
        var integration = MakeWebhookIntegration(tenant.Id);
        var body = Encoding.UTF8.GetBytes("{}");
        const string secret = "whs_secret";
        // Authentic signature, but the timestamp is 10 minutes old — outside the 5-minute window.
        var staleTs = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600).ToString();

        _repository.FindAsync("acme", "orders").Returns((tenant, integration));
        _encryption.Decrypt(integration.EncryptedWebhookSecret!).Returns(secret);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _handler.HandleAsync(
            new DeliverWebhookCommand("acme", "orders", Signature(secret, staleTs, body), staleTs, "d1", body)));

        await _repository.Received(1).RecordDeliveryAsync(Arg.Is<WebhookDelivery>(d =>
            d.TenantId == tenant.Id
            && d.IntegrationId == integration.Id
            && d.Outcome == WebhookDeliveryOutcome.Expired));
        // A replay must never be queued.
        await _repository.DidNotReceive().CreateWorkItemAsync(Arg.Any<WorkItem>());
    }

    [Fact]
    public async Task HandleAsync_MissingTimestamp_IsRejectedAsExpired()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "acme" };
        var integration = MakeWebhookIntegration(tenant.Id);
        var body = Encoding.UTF8.GetBytes("{}");
        const string secret = "whs_secret";

        _repository.FindAsync("acme", "orders").Returns((tenant, integration));
        _encryption.Decrypt(integration.EncryptedWebhookSecret!).Returns(secret);

        // Sign with an empty timestamp so the signature itself matches, isolating the freshness check.
        await Assert.ThrowsAsync<UnauthorizedException>(() => _handler.HandleAsync(
            new DeliverWebhookCommand("acme", "orders", Signature(secret, "", body), null, "d1", body)));

        await _repository.Received(1).RecordDeliveryAsync(Arg.Is<WebhookDelivery>(d =>
            d.Outcome == WebhookDeliveryOutcome.Expired));
    }

    [Fact]
    public async Task HandleAsync_DisabledIntegration_ThrowsNotFound()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "acme" };
        var integration = MakeWebhookIntegration(tenant.Id);
        integration.Status = IntegrationStatus.Disabled;

        _repository.FindAsync("acme", "orders").Returns((tenant, integration));

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(
            new DeliverWebhookCommand("acme", "orders", "sha256=anything", Now(), null, [])));
    }

    [Fact]
    public async Task HandleAsync_DuplicateDeliveryId_DoesNotQueueAgain()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "acme" };
        var integration = MakeWebhookIntegration(tenant.Id);
        var body = Encoding.UTF8.GetBytes("{}");
        const string secret = "whs_secret";
        var ts = Now();

        _repository.FindAsync("acme", "orders").Returns((tenant, integration));
        _encryption.Decrypt(integration.EncryptedWebhookSecret!).Returns(secret);
        _repository.DeliveryExistsAsync(tenant.Id, integration.Id, "delivery-1").Returns(true);

        var result = await _handler.HandleAsync(new DeliverWebhookCommand(
            "acme",
            "orders",
            Signature(secret, ts, body),
            ts,
            "delivery-1",
            body));

        Assert.False(result.Queued);
        Assert.Equal(Guid.Empty, result.WorkItemId);
        await _repository.DidNotReceive().CreateWorkItemAsync(Arg.Any<WorkItem>());
        await _repository.Received(1).RecordDeliveryAsync(Arg.Is<WebhookDelivery>(d =>
            d.TenantId == tenant.Id
            && d.IntegrationId == integration.Id
            && d.DeliveryId == "delivery-1"
            && d.Outcome == WebhookDeliveryOutcome.Deduplicated));
    }

    [Fact]
    public async Task HandleAsync_ConcurrentDuplicateLosesUniqueRace_DoesNotQueue()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "acme" };
        var integration = MakeWebhookIntegration(tenant.Id);
        var body = Encoding.UTF8.GetBytes("{}");
        const string secret = "whs_secret";
        var ts = Now();

        _repository.FindAsync("acme", "orders").Returns((tenant, integration));
        _encryption.Decrypt(integration.EncryptedWebhookSecret!).Returns(secret);
        // Fast-path check passes, but the unique index rejects the insert race.
        _repository.DeliveryExistsAsync(tenant.Id, integration.Id, "delivery-1").Returns(false);
        _repository.CreateWorkItemAsync(Arg.Any<WorkItem>()).Returns((WorkItem?)null);

        var result = await _handler.HandleAsync(new DeliverWebhookCommand(
            "acme", "orders", Signature(secret, ts, body), ts, "delivery-1", body));

        Assert.False(result.Queued);
        Assert.Equal(Guid.Empty, result.WorkItemId);
        await _repository.Received(1).RecordDeliveryAsync(Arg.Is<WebhookDelivery>(d =>
            d.TenantId == tenant.Id
            && d.IntegrationId == integration.Id
            && d.DeliveryId == "delivery-1"
            && d.Outcome == WebhookDeliveryOutcome.Deduplicated));
    }

    [Fact]
    public async Task HandleAsync_UnknownIntegration_ThrowsNotFound()
    {
        _repository.FindAsync("acme", "ghost").Returns(((Tenant, Integration)?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(
            new DeliverWebhookCommand("acme", "ghost", "sha256=x", Now(), null, [])));
    }

    [Fact]
    public async Task HandleAsync_OversizedPayload_ThrowsValidation()
    {
        var body = new byte[DeliverWebhookHandler.MaxPayloadBytes + 1];

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(
            new DeliverWebhookCommand("acme", "orders", "sha256=x", Now(), null, body)));
    }

    private static Integration MakeWebhookIntegration(Guid tenantId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = "Orders",
        Slug = "orders",
        Environment = "production",
        Status = IntegrationStatus.Enabled,
        TriggerType = TriggerType.Webhook,
        ClassName = "Acme.Orders",
        EncryptedWebhookSecret = "encrypted"
    };

    private static string Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

    // Signs over "{timestamp}.{body}" to match the handler's replay-protected scheme.
    private static string Signature(string secret, string timestamp, byte[] body)
    {
        var payload = Encoding.UTF8.GetBytes($"{timestamp}.").Concat(body).ToArray();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
