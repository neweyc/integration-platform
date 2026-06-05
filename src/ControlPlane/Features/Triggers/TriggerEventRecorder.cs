using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Triggers;

public record TriggerEventRecord(
    Guid TenantId,
    Guid IntegrationId,
    string AdapterKey,
    TriggerSource Source,
    TriggerEventOutcome Outcome,
    DateTime ReceivedAt,
    Guid? IntegrationTriggerId = null,
    string? EventKey = null,
    Guid? WorkItemId = null,
    string? MetadataJson = null,
    string? ErrorMessage = null);

public interface ITriggerEventRecorder
{
    Task<TriggerEvent> RecordAsync(TriggerEventRecord record, CancellationToken ct = default);
}

public class TriggerEventRecorder(AppDbContext db) : ITriggerEventRecorder
{
    public async Task<TriggerEvent> RecordAsync(TriggerEventRecord record, CancellationToken ct = default)
    {
        var triggerEvent = new TriggerEvent
        {
            TenantId = record.TenantId,
            IntegrationId = record.IntegrationId,
            IntegrationTriggerId = record.IntegrationTriggerId,
            AdapterKey = NormalizeAdapterKey(record.AdapterKey, record.Source),
            Source = record.Source,
            EventKey = record.EventKey,
            Outcome = record.Outcome,
            WorkItemId = record.WorkItemId,
            MetadataJson = record.MetadataJson,
            ErrorMessage = record.ErrorMessage,
            ReceivedAt = record.ReceivedAt
        };

        db.TriggerEvents.Add(triggerEvent);
        await db.SaveChangesAsync(ct);
        return triggerEvent;
    }

    internal static string NormalizeAdapterKey(string? adapterKey, TriggerSource source) =>
        string.IsNullOrWhiteSpace(adapterKey)
            ? source.ToString().ToLowerInvariant()
            : adapterKey.Trim().ToLowerInvariant();
}
