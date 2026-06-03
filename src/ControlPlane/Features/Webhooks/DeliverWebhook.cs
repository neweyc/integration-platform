using System.Security.Cryptography;
using System.Text;
using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Webhooks;

public record DeliverWebhookCommand(
    string TenantSlug,
    string IntegrationSlug,
    string? SignatureHeader,
    string? DeliveryId,
    byte[] BodyBytes) : ICommand<DeliverWebhookResult>;

public record DeliverWebhookResult(Guid WorkItemId, bool Queued);

public interface IWebhookRepository
{
    Task<(Tenant Tenant, Integration Integration)?> FindAsync(
        string tenantSlug, string integrationSlug, CancellationToken ct = default);

    Task<bool> DeliveryExistsAsync(
        Guid tenantId, string deliveryId, CancellationToken ct = default);

    // Returns null if a concurrent delivery with the same DeliveryId already inserted
    // (unique-index race backstop), otherwise the persisted work item.
    Task<WorkItem?> CreateWorkItemAsync(WorkItem workItem, CancellationToken ct = default);

    Task RecordDeliveryAsync(WebhookDelivery delivery, CancellationToken ct = default);
}

public class DeliverWebhookHandler(IWebhookRepository repository, IEncryptionService encryption)
    : ICommandHandler<DeliverWebhookCommand, DeliverWebhookResult>
{
    public const long MaxPayloadBytes = 10 * 1024 * 1024; // 10 MB

    public async Task<DeliverWebhookResult> HandleAsync(
        DeliverWebhookCommand command, CancellationToken ct = default)
    {
        if (command.BodyBytes.LongLength > MaxPayloadBytes)
            throw new ValidationException("Webhook payload exceeds the 10 MB limit.");

        // Always 404 for unknown or disabled endpoints to avoid leaking existence.
        var found = await repository.FindAsync(command.TenantSlug, command.IntegrationSlug, ct);

        if (found is null
            || found.Value.Integration.TriggerType != TriggerType.Webhook
            || found.Value.Integration.Status != IntegrationStatus.Enabled
            || string.IsNullOrEmpty(found.Value.Integration.EncryptedWebhookSecret))
            throw new NotFoundException("Webhook endpoint not found.");

        var (tenant, integration) = found.Value;

        if (!VerifySignature(command.SignatureHeader, command.BodyBytes, integration.EncryptedWebhookSecret))
        {
            await RecordDeliveryAsync(tenant, integration, command.DeliveryId,
                WebhookDeliveryOutcome.InvalidSignature, workItemId: null, ct);
            throw new UnauthorizedException("Invalid webhook signature.");
        }

        // Idempotency fast path: skip if this delivery ID was already processed.
        if (command.DeliveryId is not null
            && await repository.DeliveryExistsAsync(tenant.Id, command.DeliveryId, ct))
        {
            await RecordDeliveryAsync(tenant, integration, command.DeliveryId,
                WebhookDeliveryOutcome.Deduplicated, workItemId: null, ct);
            return new DeliverWebhookResult(Guid.Empty, Queued: false);
        }

        var workItem = new WorkItem
        {
            TenantId = tenant.Id,
            IntegrationId = integration.Id,
            Environment = integration.Environment,
            TriggerSource = TriggerSource.Webhook,
            Status = WorkItemStatus.Pending,
            AvailableAt = DateTime.UtcNow,
            Payload = Encoding.UTF8.GetString(command.BodyBytes),
            DeliveryId = command.DeliveryId
        };

        // Race backstop: the unique (TenantId, DeliveryId) index rejects a concurrent
        // duplicate that slipped past the fast-path check above; treat that as deduped.
        var created = await repository.CreateWorkItemAsync(workItem, ct);

        if (created is null)
        {
            await RecordDeliveryAsync(tenant, integration, command.DeliveryId,
                WebhookDeliveryOutcome.Deduplicated, workItemId: null, ct);
            return new DeliverWebhookResult(Guid.Empty, Queued: false);
        }

        await RecordDeliveryAsync(tenant, integration, command.DeliveryId,
            WebhookDeliveryOutcome.Accepted, created.Id, ct);

        return new DeliverWebhookResult(created.Id, Queued: true);
    }

    private Task RecordDeliveryAsync(
        Tenant tenant,
        Integration integration,
        string? deliveryId,
        WebhookDeliveryOutcome outcome,
        Guid? workItemId,
        CancellationToken ct) =>
        repository.RecordDeliveryAsync(
            new WebhookDelivery
            {
                TenantId = tenant.Id,
                IntegrationId = integration.Id,
                DeliveryId = deliveryId,
                Outcome = outcome,
                WorkItemId = workItemId,
                ReceivedAt = DateTime.UtcNow
            },
            ct);

    private bool VerifySignature(string? signatureHeader, byte[] bodyBytes, string encryptedSecret)
    {
        if (string.IsNullOrEmpty(signatureHeader))
            return false;

        if (!signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            return false;

        var receivedHex = signatureHeader["sha256=".Length..];
        if (receivedHex.Length == 0)
            return false;

        string secret;
        try
        {
            secret = encryption.Decrypt(encryptedSecret);
        }
        catch
        {
            return false;
        }

        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var expectedHash = HMACSHA256.HashData(secretBytes, bodyBytes);
        var expectedHex = Convert.ToHexString(expectedHash).ToLowerInvariant();
        var receivedNormalized = receivedHex.ToLowerInvariant();

        if (receivedNormalized.Length != expectedHex.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedHex),
            Encoding.ASCII.GetBytes(receivedNormalized));
    }
}
