using Serto.Sdk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Serto.Testing;

public class TestIntegrationContext : IIntegrationContext
{
    public IReadOnlyDictionary<string, string> Secrets { get; set; } = new Dictionary<string, string>();
    public ILogger Logger { get; set; } = NullLogger.Instance;
    public HttpClient Http { get; set; } = new();
    public ExecutionMetadata Execution { get; set; } = new(
        Guid.NewGuid(), Guid.NewGuid(), "Test Integration", "local", DateTime.UtcNow);
    public string? Payload { get; set; }

    /// <summary>Captures messages the integration publishes so a test can assert on them.</summary>
    public RecordingMessagePublisher Published { get; set; } = new();
    public IMessagePublisher Messages => Published;

    /// <summary>How the run-under-test was triggered. Defaults to a manual trigger.</summary>
    public TriggerInfo Trigger { get; set; } = new ManualTrigger();
}

/// <summary>An <see cref="IMessagePublisher"/> that records published messages instead of sending them.</summary>
public sealed class RecordingMessagePublisher : IMessagePublisher
{
    private readonly List<PublishedMessage> _messages = [];

    /// <summary>The messages published during the run, in order.</summary>
    public IReadOnlyList<PublishedMessage> Messages => _messages;

    public Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default) where TMessage : class
    {
        _messages.Add(new PublishedMessage(MessageSubject.For(typeof(TMessage)), message));
        return Task.CompletedTask;
    }

    public Task PublishAsync(string subject, object payload, CancellationToken ct = default)
    {
        _messages.Add(new PublishedMessage(subject, payload));
        return Task.CompletedTask;
    }
}

public sealed record PublishedMessage(string Subject, object Payload);

public static class IntegrationTester
{
    public static async Task RunAsync<T>(
        Dictionary<string, string>? secrets = null,
        string? payload = null,
        ILogger? logger = null) where T : IIntegration, new()
    {
        var context = new TestIntegrationContext
        {
            Secrets = secrets ?? new Dictionary<string, string>(),
            Payload = payload,
            Logger = logger ?? NullLogger.Instance
        };

        var integration = new T();
        await integration.RunAsync(context, CancellationToken.None);
    }
}
