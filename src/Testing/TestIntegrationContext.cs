using IntegrationPlatform.Sdk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntegrationPlatform.Testing;

public class TestIntegrationContext : IIntegrationContext
{
    public IReadOnlyDictionary<string, string> Secrets { get; set; } = new Dictionary<string, string>();
    public ILogger Logger { get; set; } = NullLogger.Instance;
    public HttpClient Http { get; set; } = new();
    public ExecutionMetadata Execution { get; set; } = new(
        Guid.NewGuid(), Guid.NewGuid(), "Test Integration", "local", DateTime.UtcNow);
    public string? Payload { get; set; }
}

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
