using Serto.Sdk;
using Microsoft.Extensions.Logging;

namespace RuntimeAgent.Execution;

public class ExecutionContext(
    IReadOnlyDictionary<string, string> secrets,
    ILogger logger,
    HttpClient http,
    ExecutionMetadata metadata,
    IMessagePublisher messages,
    TriggerInfo trigger,
    string? payload = null) : IIntegrationContext
{
    public IReadOnlyDictionary<string, string> Secrets { get; } = secrets;
    public ILogger Logger { get; } = logger;
    public HttpClient Http { get; } = http;
    public ExecutionMetadata Execution { get; } = metadata;
    public IMessagePublisher Messages { get; } = messages;
    public TriggerInfo Trigger { get; } = trigger;
    public string? Payload { get; } = payload;
}
