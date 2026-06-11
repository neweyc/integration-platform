using System.Text.Json;

namespace RuntimeAgent.Execution;

// The agent's side of the agent↔integration wire protocol. The agent serializes a WireInvocation onto
// the process's stdin and parses WireEvent lines (JSON-lines) from its stdout. Language SDKs implement
// the mirror image of these shapes. The canonical contract is docs/multi-language-runtimes.md.

public static class WireProtocol
{
    public const string Version = "1";

    // camelCase on the wire; tolerant on read so an SDK that omits optional fields still parses.
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

// Sent to the integration on stdin (one object, then EOF). Secrets ride inside the payload rather than
// the environment so they never reach the process table or grandchild processes.
public sealed record WireInvocation(
    string ProtocolVersion,
    string Entrypoint,
    WireExecution Execution,
    WireTrigger Trigger,
    string? Payload,
    IReadOnlyDictionary<string, string> Secrets);

public sealed record WireExecution(
    Guid ExecutionId,
    Guid IntegrationId,
    string IntegrationName,
    string Environment,
    DateTime ScheduledAt);

public sealed record WireTrigger(
    string Type,
    string? Cron = null,
    string? Subject = null,
    string? DeliveryId = null,
    Guid? MessageId = null,
    DateTime? PublishedAt = null);

// One stdout line from the integration. A single shape with optional fields keyed by Type, rather than a
// polymorphic hierarchy, keeps parsing trivial and forgiving across languages.
//   type = "log"     → Level, Message, Exception, Properties
//   type = "message" → Subject, Body
//   type = "result"  → Succeeded, Error
public sealed record WireEvent(
    string Type,
    string? Level = null,
    string? Message = null,
    string? Exception = null,
    string? Subject = null,
    string? Body = null,
    bool? Succeeded = null,
    string? Error = null);

// Thrown by a runner when an out-of-process integration reports or implies failure. Carries a
// human-readable message that the shared lifecycle reports to the control plane as the run's error.
public sealed class IntegrationRunException(string message) : Exception(message);
