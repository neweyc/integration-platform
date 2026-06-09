using System.Threading.Channels;

namespace ControlPlane.Features.Alerts;

// Hands failure alerts from request-path code (CompleteExecutionHandler) to the background dispatcher.
// In-process and best-effort: alerts queued when the process stops are lost, which is an acceptable
// trade-off for notifications — the execution itself is already durably recorded as Failed.
public interface IAlertDispatchQueue
{
    void Enqueue(FailedExecutionAlert alert);
}

public class AlertDispatchQueue : IAlertDispatchQueue
{
    // Bounded so a storm of failures can never grow memory without limit; the oldest alert is dropped
    // when full. A single integration failing in a tight loop should not knock the control plane over.
    private readonly Channel<FailedExecutionAlert> channel = Channel.CreateBounded<FailedExecutionAlert>(
        new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    public ChannelReader<FailedExecutionAlert> Reader => channel.Reader;

    public void Enqueue(FailedExecutionAlert alert) => channel.Writer.TryWrite(alert);
}
