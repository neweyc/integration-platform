using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared.Domain;

namespace ControlPlane.Features.Triggers;

public record TriggerWorkItemRequest(
    Guid TenantId,
    Guid IntegrationId,
    string Environment,
    TriggerSource TriggerSource,
    DateTime AvailableAt,
    Guid? IntegrationTriggerId = null,
    string? AdapterKey = null,
    DateTime? ReceivedAt = null,
    string? Payload = null,
    string? DeliveryId = null,
    string? MetadataJson = null,
    Guid? ManualRunRequestId = null,
    Guid? WorkflowNodeId = null,
    int AttemptNumber = 1,
    Guid? ParentExecutionId = null,
    Guid? RootExecutionId = null,
    Guid? MessageId = null);

public enum TriggerWorkItemOutcome
{
    ConvertedToWork,
    Deduplicated
}

public record TriggerWorkItemResult(TriggerWorkItemOutcome Outcome, WorkItem? WorkItem, TriggerEvent? TriggerEvent = null);

public interface ITriggerWorkItemProducer
{
    Task<TriggerWorkItemResult> EnqueueAsync(TriggerWorkItemRequest request, CancellationToken ct = default);
}

public class TriggerWorkItemProducer(AppDbContext db) : ITriggerWorkItemProducer
{
    public async Task<TriggerWorkItemResult> EnqueueAsync(TriggerWorkItemRequest request, CancellationToken ct = default)
    {
        var workItem = new WorkItem
        {
            TenantId = request.TenantId,
            IntegrationId = request.IntegrationId,
            IntegrationTriggerId = request.IntegrationTriggerId,
            Environment = request.Environment,
            TriggerSource = request.TriggerSource,
            Status = WorkItemStatus.Pending,
            AvailableAt = request.AvailableAt,
            Payload = request.Payload,
            DeliveryId = request.DeliveryId,
            ManualRunRequestId = request.ManualRunRequestId,
            WorkflowNodeId = request.WorkflowNodeId,
            AttemptNumber = request.AttemptNumber,
            ParentExecutionId = request.ParentExecutionId,
            RootExecutionId = request.RootExecutionId,
            MessageId = request.MessageId
        };

        db.WorkItems.Add(workItem);
        var triggerEvent = CreateEvent(request, TriggerEventOutcome.ConvertedToWork, workItem.Id);
        db.TriggerEvents.Add(triggerEvent);

        try
        {
            await db.SaveChangesAsync(ct);
            return new TriggerWorkItemResult(TriggerWorkItemOutcome.ConvertedToWork, workItem, triggerEvent);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            db.Entry(workItem).State = EntityState.Detached;
            db.Entry(triggerEvent).State = EntityState.Detached;

            var deduped = CreateEvent(
                request,
                TriggerEventOutcome.Deduplicated,
                workItemId: null,
                errorMessage: "Duplicate trigger event was deduplicated before work item creation.");

            db.TriggerEvents.Add(deduped);
            await db.SaveChangesAsync(ct);

            return new TriggerWorkItemResult(TriggerWorkItemOutcome.Deduplicated, null, deduped);
        }
    }

    private static TriggerEvent CreateEvent(
        TriggerWorkItemRequest request,
        TriggerEventOutcome outcome,
        Guid? workItemId,
        string? errorMessage = null) =>
        TriggerEventRecorder.Create(
            new TriggerEventRecord(
                request.TenantId,
                request.IntegrationId,
                request.AdapterKey ?? request.TriggerSource.ToString(),
                request.TriggerSource,
                outcome,
                request.ReceivedAt ?? request.AvailableAt,
                IntegrationTriggerId: request.IntegrationTriggerId,
                EventKey: request.DeliveryId,
                WorkItemId: workItemId,
                MetadataJson: request.MetadataJson,
                ErrorMessage: errorMessage));
}
