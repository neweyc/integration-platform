using System.Text.Json;
using RuntimeAgent.Agent;
using Serto.Sdk;

namespace RuntimeAgent.Execution;

// Publishes messages from a running integration to the control plane, which fans each one out to the
// integrations that subscribe to its subject. Bound to the publishing execution so every message
// carries lineage back to the run that raised it.
public class AgentMessagePublisher(IControlPlaneClient controlPlane, Guid sourceExecutionId) : IMessagePublisher
{
    public Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default) where TMessage : class
    {
        var subject = MessageSubject.For(typeof(TMessage));
        var body = JsonSerializer.Serialize(message, MessageJson.Options);
        return controlPlane.PublishMessageAsync(subject, body, sourceExecutionId, ct);
    }

    public Task PublishAsync(string subject, object payload, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(payload, MessageJson.Options);
        return controlPlane.PublishMessageAsync(subject, body, sourceExecutionId, ct);
    }
}
