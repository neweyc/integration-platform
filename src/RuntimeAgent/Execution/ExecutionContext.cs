using IntegrationPlatform.Sdk;
using Microsoft.Extensions.Logging;

namespace RuntimeAgent.Execution;

public class ExecutionContext(
    IReadOnlyDictionary<string, string> secrets,
    ILogger logger,
    HttpClient http,
    ExecutionMetadata metadata) : IIntegrationContext
{
    public IReadOnlyDictionary<string, string> Secrets { get; } = secrets;
    public ILogger Logger { get; } = logger;
    public HttpClient Http { get; } = http;
    public ExecutionMetadata Execution { get; } = metadata;
}
