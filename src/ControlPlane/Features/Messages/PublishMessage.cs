using ControlPlane.Features.Triggers;
using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Messages;

// Body sent by an agent when an integration publishes a message.
public record PublishMessageRequest(string Subject, string? Body, Guid? SourceExecutionId);

public record PublishMessageCommand(
    Guid TenantId,
    string Environment,
    string Subject,
    string? Body,
    Guid? SourceExecutionId) : ICommand<PublishMessageResult>;

public record PublishMessageResult(Guid MessageId, int SubscriberCount);

// Delivers a published message: persists the envelope, then fans it out to every integration that
// subscribes to the subject (an enabled Queue trigger on an enabled integration in the same tenant +
// environment). Each subscriber gets its own Queue work item via the shared trigger-work-item
// producer, carrying the body as payload, a per-(subscriber, message) dedup key, and parent/root
// execution lineage from the publishing execution. The existing poll/claim path dispatches the work.
public class PublishMessageHandler(
    AppDbContext db,
    ITriggerWorkItemProducer workItemProducer)
    : ICommandHandler<PublishMessageCommand, PublishMessageResult>
{
    public async Task<PublishMessageResult> HandleAsync(PublishMessageCommand command, CancellationToken ct = default)
    {
        var subject = command.Subject?.Trim();
        if (string.IsNullOrEmpty(subject))
            throw new ValidationException("Message subject is required.");

        var publishedAt = DateTime.UtcNow;

        // The canonical envelope. Storage format is not the delivery format: subscribers receive the
        // raw body on their work item, not this record.
        var message = new Message
        {
            TenantId = command.TenantId,
            Environment = command.Environment,
            Subject = subject,
            Body = command.Body,
            SourceExecutionId = command.SourceExecutionId,
            PublishedAt = publishedAt
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);

        var subscribers = await db.IntegrationTriggers
            .Include(t => t.Integration)
            .Where(t => t.TenantId == command.TenantId
                     && t.Type == TriggerType.Queue
                     && t.Enabled
                     && t.Subject == subject
                     && t.Integration.Status == IntegrationStatus.Enabled
                     && t.Integration.Environment == command.Environment)
            .ToListAsync(ct);

        var rootExecutionId = await ResolveRootExecutionAsync(command.TenantId, command.SourceExecutionId, ct);

        foreach (var subscriber in subscribers)
        {
            await workItemProducer.EnqueueAsync(
                new TriggerWorkItemRequest(
                    command.TenantId,
                    subscriber.IntegrationId,
                    command.Environment,
                    TriggerSource.Queue,
                    publishedAt,
                    IntegrationTriggerId: subscriber.Id,
                    AdapterKey: "message",
                    ReceivedAt: publishedAt,
                    Payload: command.Body,
                    // Idempotency: a retried delivery of the same message to the same subscriber is
                    // deduped by the existing (TenantId, IntegrationTriggerId, DeliveryId) unique index.
                    DeliveryId: message.Id.ToString(),
                    ParentExecutionId: command.SourceExecutionId,
                    RootExecutionId: rootExecutionId,
                    MessageId: message.Id),
                ct);
        }

        return new PublishMessageResult(message.Id, subscribers.Count);
    }

    // Lineage: thread the publishing execution's root through to each subscriber. Falls back to the
    // publishing execution itself when it has no recorded root (it is the root).
    private async Task<Guid?> ResolveRootExecutionAsync(Guid tenantId, Guid? sourceExecutionId, CancellationToken ct)
    {
        if (sourceExecutionId is not { } sourceId)
            return null;

        var root = await db.ExecutionRecords
            .Where(e => e.TenantId == tenantId && e.Id == sourceId)
            .Select(e => e.RootExecutionId)
            .FirstOrDefaultAsync(ct);

        return root ?? sourceId;
    }
}
