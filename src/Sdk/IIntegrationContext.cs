using Microsoft.Extensions.Logging;

namespace IntegrationPlatform.Sdk;

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
}

public record ExecutionMetadata(
    Guid ExecutionId,
    Guid IntegrationId,
    string IntegrationName,
    string Environment,
    DateTime ScheduledAt);
