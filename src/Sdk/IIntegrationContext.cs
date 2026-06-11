using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Serto.Sdk;

/// <summary>
/// Injected into every integration run. Provides secrets, logging, and execution metadata.
/// </summary>
public interface IIntegrationContext
{
    /// <summary>Decrypted secrets for the integration's environment.</summary>
    IReadOnlyDictionary<string, string> Secrets { get; }

    /// <summary>Structured logger. Output is captured and stored in execution history.</summary>
    ILogger Logger { get; }

    /// <summary>Pre-configured HttpClient for outbound calls.</summary>
    HttpClient Http { get; }

    /// <summary>Metadata about the current execution.</summary>
    ExecutionMetadata Execution { get; }

    /// <summary>
    /// Raw request body for Webhook- and Message-triggered executions. Null for Scheduled and Manual
    /// triggers. For a message this is the published body verbatim — use <see cref="PayloadAs{T}"/>
    /// to deserialize it.
    /// </summary>
    string? Payload { get; }

    /// <summary>Publishes messages other integrations can subscribe to.</summary>
    IMessagePublisher Messages { get; }

    /// <summary>
    /// How this run was triggered. Pattern-match the concrete type (e.g. <see cref="MessageTrigger"/>)
    /// for source-specific details.
    /// </summary>
    TriggerInfo Trigger { get; }

    /// <summary>
    /// Deserialize <see cref="Payload"/> into <typeparamref name="T"/>. Returns default when there is
    /// no payload. Works for both webhook and message bodies.
    /// </summary>
    T? PayloadAs<T>() =>
        string.IsNullOrEmpty(Payload) ? default : JsonSerializer.Deserialize<T>(Payload, MessageJson.Options);
}

public record ExecutionMetadata(
    Guid ExecutionId,
    Guid IntegrationId,
    string IntegrationName,
    string Environment,
    DateTime ScheduledAt);
