using System.Text.Json;
using RuntimeAgent.Agent;

namespace RuntimeAgent.Execution;

public class ControlPlaneExecutionLogger(
    ILogger inner,
    IControlPlaneClient controlPlane,
    Guid executionId) : ILogger
{
    private readonly List<ExecutionLogEntry> _buffer = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        inner.Log(logLevel, eventId, state, exception, formatter);

        if (logLevel == LogLevel.None)
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message))
            return;

        var log = new ExecutionLogEntry(
            DateTime.UtcNow,
            logLevel.ToString(),
            message,
            exception?.ToString(),
            SerializeProperties(state, eventId));

        _buffer.Add(log);
    }

    public async Task FlushAsync(CancellationToken flushToken)
    {
        foreach (var log in _buffer)
            await controlPlane.RecordLogAsync(executionId, log, flushToken);

        _buffer.Clear();
    }

    private static string? SerializeProperties<TState>(TState state, EventId eventId)
    {
        var properties = new Dictionary<string, object?>();

        if (eventId.Id != 0 || !string.IsNullOrWhiteSpace(eventId.Name))
        {
            properties["EventId"] = eventId.Id;
            properties["EventName"] = eventId.Name;
        }

        if (state is IEnumerable<KeyValuePair<string, object?>> values)
        {
            foreach (var value in values)
            {
                if (value.Key == "{OriginalFormat}")
                    continue;

                properties[value.Key] = value.Value?.ToString();
            }
        }

        return properties.Count == 0 ? null : JsonSerializer.Serialize(properties);
    }
}
