using Shared.Domain;

namespace ControlPlane.Features.Triggers;

public record TriggerAdapterDescriptor(
    string Key,
    string DisplayName,
    TriggerSource Source,
    TriggerType? TriggerType,
    bool RequiresStoredTrigger,
    bool SupportsPayload,
    bool SupportsDeduplication,
    string Description);

public interface ITriggerAdapter
{
    TriggerAdapterDescriptor Descriptor { get; }
}

public interface ITriggerAdapterCatalog
{
    IReadOnlyList<TriggerAdapterDescriptor> List();
    TriggerAdapterDescriptor? Find(string key);
}

public class TriggerAdapterCatalog(IEnumerable<ITriggerAdapter> adapters) : ITriggerAdapterCatalog
{
    private readonly IReadOnlyList<TriggerAdapterDescriptor> _descriptors = adapters
        .Select(a => a.Descriptor)
        .OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<TriggerAdapterDescriptor> List() => _descriptors;

    public TriggerAdapterDescriptor? Find(string key) =>
        _descriptors.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
}

public sealed class ScheduledTriggerAdapter : ITriggerAdapter
{
    public TriggerAdapterDescriptor Descriptor { get; } = new(
        "scheduled",
        "Scheduled",
        TriggerSource.Scheduled,
        TriggerType.Scheduled,
        RequiresStoredTrigger: true,
        SupportsPayload: false,
        SupportsDeduplication: false,
        "Evaluates cron state for enabled scheduled triggers and creates due work items.");
}

public sealed class ManualTriggerAdapter : ITriggerAdapter
{
    public TriggerAdapterDescriptor Descriptor { get; } = new(
        "manual",
        "Manual",
        TriggerSource.Manual,
        TriggerType.Manual,
        RequiresStoredTrigger: false,
        SupportsPayload: false,
        SupportsDeduplication: false,
        "Converts an operator run request into a pending work item.");
}

public sealed class WebhookTriggerAdapter : ITriggerAdapter
{
    public TriggerAdapterDescriptor Descriptor { get; } = new(
        "webhook",
        "Webhook",
        TriggerSource.Webhook,
        TriggerType.Webhook,
        RequiresStoredTrigger: true,
        SupportsPayload: true,
        SupportsDeduplication: true,
        "Validates signed inbound webhooks, records delivery state, and creates pending work items.");
}

public sealed class QueueTriggerAdapter : ITriggerAdapter
{
    public TriggerAdapterDescriptor Descriptor { get; } = new(
        "queue",
        "Queue",
        TriggerSource.Queue,
        TriggerType.Queue,
        RequiresStoredTrigger: true,
        SupportsPayload: true,
        SupportsDeduplication: true,
        "Future adapter for queue and event-bus messages such as SQS, Azure Service Bus, RabbitMQ, and Kafka.");
}

public sealed class FileTriggerAdapter : ITriggerAdapter
{
    public TriggerAdapterDescriptor Descriptor { get; } = new(
        "file",
        "File",
        TriggerSource.File,
        TriggerType.File,
        RequiresStoredTrigger: true,
        SupportsPayload: true,
        SupportsDeduplication: true,
        "Future adapter for file and object arrivals such as SFTP, watched folders, S3, Azure Blob, and GCS.");
}
