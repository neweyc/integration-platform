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
    string? TimestampHeader,
    string? DeliveryId,
    byte[] BodyBytes) : ICommand<DeliverWebhookResult>;

public record DeliverWebhookResult(Guid WorkItemId, bool Queued);

public interface IWebhookRepository
{
    Task<(Tenant Tenant, Integration Integration)?> FindAsync(
        string tenantSlug, string integrationSlug, CancellationToken ct = default);

    Task<bool> DeliveryExistsAsync(
        Guid tenantId, Guid integrationId, string deliveryId, CancellationToken ct = default);

    // Returns null if a concurrent delivery with the same integration-scoped DeliveryId
    // already inserted (unique-index race backstop), otherwise the persisted work item.
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

        // The signature covers "{timestamp}.{body}", so a valid signature proves the sender
        // knew the secret AND committed to that timestamp (it cannot have been tampered).
        var signedPayload = BuildSignedPayload(command.TimestampHeader, command.BodyBytes);
        if (!VerifySignature(command.SignatureHeader, signedPayload, integration.EncryptedWebhookSecret))
        {
            await RecordDeliveryAsync(tenant, integration, command.DeliveryId,
                WebhookDeliveryOutcome.InvalidSignature, workItemId: null, ct);
            throw new UnauthorizedException("Invalid webhook signature.");
        }

        // Replay protection: reject authentic-but-stale deliveries outside the tolerance window.
        if (!IsTimestampFresh(command.TimestampHeader, DateTimeOffset.UtcNow))
        {
            await RecordDeliveryAsync(tenant, integration, command.DeliveryId,
                WebhookDeliveryOutcome.Expired, workItemId: null, ct);
            throw new UnauthorizedException("Webhook timestamp is missing or outside the allowed window.");
        }

        // Idempotency fast path: skip if this delivery ID was already processed.
        if (command.DeliveryId is not null
            && await repository.DeliveryExistsAsync(tenant.Id, integration.Id, command.DeliveryId, ct))
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

        // Race backstop: the unique (TenantId, IntegrationId, DeliveryId) index rejects a
        // concurrent duplicate that slipped past the fast-path check above; treat it as deduped.
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

    // Signed payload = "{timestamp}.{body}". A missing timestamp yields ".{body}", which a
    // correctly-signed request will never match, so the signature check rejects it.
    private static byte[] BuildSignedPayload(string? timestampHeader, byte[] bodyBytes)
    {
        var prefix = Encoding.UTF8.GetBytes($"{timestampHeader}.");
        var payload = new byte[prefix.Length + bodyBytes.Length];
        Buffer.BlockCopy(prefix, 0, payload, 0, prefix.Length);
        Buffer.BlockCopy(bodyBytes, 0, payload, prefix.Length, bodyBytes.Length);
        return payload;
    }

    private static bool IsTimestampFresh(string? timestampHeader, DateTimeOffset now)
    {
        if (!long.TryParse(timestampHeader, out var timestamp))
            return false;

        var deltaSeconds = Math.Abs(now.ToUnixTimeSeconds() - timestamp);
        return deltaSeconds <= WebhookHeaders.ToleranceSeconds;
    }

    private bool VerifySignature(string? signatureHeader, byte[] signedPayload, string encryptedSecret)
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
        var expectedHash = HMACSHA256.HashData(secretBytes, signedPayload);
        var expectedHex = Convert.ToHexString(expectedHash).ToLowerInvariant();
        var receivedNormalized = receivedHex.ToLowerInvariant();

        if (receivedNormalized.Length != expectedHex.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedHex),
            Encoding.ASCII.GetBytes(receivedNormalized));
    }
}
