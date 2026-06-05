using ControlPlane.Features.Triggers;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Triggers;

public class TriggerAdapterCatalogTests
{
    [Fact]
    public void List_ReturnsBuiltInAndFutureAdapterDescriptors()
    {
        ITriggerAdapter[] adapters =
        [
            new ScheduledTriggerAdapter(),
            new ManualTriggerAdapter(),
            new WebhookTriggerAdapter(),
            new QueueTriggerAdapter(),
            new FileTriggerAdapter()
        ];

        var catalog = new TriggerAdapterCatalog(adapters);

        var descriptors = catalog.List();

        Assert.Equal(["file", "manual", "queue", "scheduled", "webhook"], descriptors.Select(d => d.Key));

        var queue = catalog.Find("QUEUE");
        Assert.NotNull(queue);
        Assert.Equal(TriggerSource.Queue, queue.Source);
        Assert.Equal(TriggerType.Queue, queue.TriggerType);
        Assert.True(queue.RequiresStoredTrigger);
        Assert.True(queue.SupportsPayload);
        Assert.True(queue.SupportsDeduplication);

        var manual = catalog.Find("manual");
        Assert.NotNull(manual);
        Assert.Equal(TriggerSource.Manual, manual.Source);
        Assert.False(manual.RequiresStoredTrigger);
    }
}
