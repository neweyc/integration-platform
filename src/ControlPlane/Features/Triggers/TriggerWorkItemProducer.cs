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
    string? Payload = null,
    string? DeliveryId = null,
    Guid? ManualRunRequestId = null,
    Guid? WorkflowNodeId = null,
    int AttemptNumber = 1,
    Guid? ParentExecutionId = null,
    Guid? RootExecutionId = null);

public enum TriggerWorkItemOutcome
{
    ConvertedToWork,
    Deduplicated
}

public record TriggerWorkItemResult(TriggerWorkItemOutcome Outcome, WorkItem? WorkItem);

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
            RootExecutionId = request.RootExecutionId
        };

        db.WorkItems.Add(workItem);

        try
        {
            await db.SaveChangesAsync(ct);
            return new TriggerWorkItemResult(TriggerWorkItemOutcome.ConvertedToWork, workItem);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            db.Entry(workItem).State = EntityState.Detached;
            return new TriggerWorkItemResult(TriggerWorkItemOutcome.Deduplicated, null);
        }
    }
}
