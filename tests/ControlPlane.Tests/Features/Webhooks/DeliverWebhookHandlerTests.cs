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
            Signature(secret, body),
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
            new DeliverWebhookCommand("acme", "orders", "sha256=bad", null, body)));

        await _repository.Received(1).RecordDeliveryAsync(Arg.Is<WebhookDelivery>(d =>
            d.TenantId == tenant.Id
            && d.IntegrationId == integration.Id
            && d.Outcome == WebhookDeliveryOutcome.InvalidSignature));
    }

    [Fact]
    public async Task HandleAsync_DisabledIntegration_ThrowsNotFound()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "acme" };
        var integration = MakeWebhookIntegration(tenant.Id);
        integration.Status = IntegrationStatus.Disabled;

        _repository.FindAsync("acme", "orders").Returns((tenant, integration));

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(
            new DeliverWebhookCommand("acme", "orders", "sha256=anything", null, [])));
    }

    [Fact]
    public async Task HandleAsync_DuplicateDeliveryId_DoesNotQueueAgain()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "acme" };
        var integration = MakeWebhookIntegration(tenant.Id);
        var body = Encoding.UTF8.GetBytes("{}");
        const string secret = "whs_secret";

        _repository.FindAsync("acme", "orders").Returns((tenant, integration));
        _encryption.Decrypt(integration.EncryptedWebhookSecret!).Returns(secret);
        _repository.DeliveryExistsAsync(tenant.Id, "delivery-1").Returns(true);

        var result = await _handler.HandleAsync(new DeliverWebhookCommand(
            "acme",
            "orders",
            Signature(secret, body),
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

        _repository.FindAsync("acme", "orders").Returns((tenant, integration));
        _encryption.Decrypt(integration.EncryptedWebhookSecret!).Returns(secret);
        // Fast-path check passes, but the unique index rejects the insert race.
        _repository.DeliveryExistsAsync(tenant.Id, "delivery-1").Returns(false);
        _repository.CreateWorkItemAsync(Arg.Any<WorkItem>()).Returns((WorkItem?)null);

        var result = await _handler.HandleAsync(new DeliverWebhookCommand(
            "acme", "orders", Signature(secret, body), "delivery-1", body));

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
            new DeliverWebhookCommand("acme", "ghost", "sha256=x", null, [])));
    }

    [Fact]
    public async Task HandleAsync_OversizedPayload_ThrowsValidation()
    {
        var body = new byte[DeliverWebhookHandler.MaxPayloadBytes + 1];

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(
            new DeliverWebhookCommand("acme", "orders", "sha256=x", null, body)));
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

    private static string Signature(string secret, byte[] body)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
