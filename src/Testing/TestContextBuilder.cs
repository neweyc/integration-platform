using Serto.Sdk;
using Microsoft.Extensions.Logging;

namespace Serto.Testing;

/// <summary>
/// Fluent builder for a <see cref="TestIntegrationContext"/>. Convenient when a test needs to set up
/// several pieces (secrets, payload, a fake HttpClient) without assigning each property by hand.
/// </summary>
public sealed class TestContextBuilder
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);
    private ILogger? _logger;
    private HttpClient? _http;
    private string? _payload;
    private ExecutionMetadata? _execution;

    public TestContextBuilder WithSecret(string key, string value)
    {
        _secrets[key] = value;
        return this;
    }

    public TestContextBuilder WithSecrets(IEnumerable<KeyValuePair<string, string>> secrets)
    {
        foreach (var pair in secrets)
            _secrets[pair.Key] = pair.Value;
        return this;
    }

    public TestContextBuilder WithPayload(string payload)
    {
        _payload = payload;
        return this;
    }

    public TestContextBuilder WithLogger(ILogger logger)
    {
        _logger = logger;
        return this;
    }

    public TestContextBuilder WithHttp(HttpClient http)
    {
        _http = http;
        return this;
    }

    public TestContextBuilder WithExecution(ExecutionMetadata execution)
    {
        _execution = execution;
        return this;
    }

    public TestIntegrationContext Build()
    {
        var context = new TestIntegrationContext
        {
            Secrets = _secrets,
            Payload = _payload
        };
        if (_logger is not null) context.Logger = _logger;
        if (_http is not null) context.Http = _http;
        if (_execution is not null) context.Execution = _execution;
        return context;
    }
}
