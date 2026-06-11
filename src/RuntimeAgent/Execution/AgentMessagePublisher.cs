using System.Text.Json;
using RuntimeAgent.Agent;
using Serto.Sdk;

namespace RuntimeAgent.Execution;

// Agent-side extension of the SDK's IMessagePublisher. In-process integrations publish through the SDK
// methods (which serialize a typed message); out-of-process integrations hand the agent a subject plus an
// already-serialized body over the wire protocol, so the subprocess/container runners publish through
// PublishRawAsync without re-serializing. The shared RunRequest carries this richer type so both paths
// use one publisher.
public interface IExecutionMessagePublisher : IMessagePublisher
{
    Task PublishRawAsync(string subject, string? body, CancellationToken ct = default);
}

// Publishes messages from a running integration to the control plane, which fans each one out to the
// integrations that subscribe to its subject. Bound to the publishing execution so every message
// carries lineage back to the run that raised it.
public class AgentMessagePublisher(IControlPlaneClient controlPlane, Guid sourceExecutionId) : IExecutionMessagePublisher
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

    // The body is already serialized JSON from the integration process — forward it verbatim rather than
    // serializing the string again (which would double-encode it).
    public Task PublishRawAsync(string subject, string? body, CancellationToken ct = default) =>
        controlPlane.PublishMessageAsync(subject, body, sourceExecutionId, ct);
}
