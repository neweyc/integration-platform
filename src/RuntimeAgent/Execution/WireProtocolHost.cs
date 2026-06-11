using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RuntimeAgent.Execution;

// Runs an integration as a child process and speaks the wire protocol with it, independent of HOW the
// process is launched: a host interpreter (SubprocessRunner) or `docker run -i <image>` (ContainerRunner)
// both produce a RuntimeLaunchSpec and hand it here. The invocation is written to the process's stdin and
// log/message/result events are read back as JSON-lines on its stdout. Outcome is surfaced to the shared
// IntegrationExecutor lifecycle the same way the in-process runner does: return = success, throw
// IntegrationRunException = failure, throw OperationCanceledException = timeout/shutdown.
internal static class WireProtocolHost
{
    // The maximum number of stderr characters retained for diagnostics on a non-zero exit.
    private const int MaxStderrChars = 4000;

    // Runs the launch spec to completion. onCancel runs (best-effort) alongside killing the local process
    // when the run is cancelled — the container runner uses it to also stop the container, which killing
    // the docker client alone would not do.
    public static async Task RunAsync(
        RuntimeLaunchSpec spec,
        RunRequest request,
        ILogger logger,
        CancellationToken ct,
        Action? onCancel = null)
    {
        var invocationJson = JsonSerializer.Serialize(BuildInvocation(request), WireProtocol.Json);

        var startInfo = new ProcessStartInfo
        {
            FileName = spec.FileName,
            WorkingDirectory = spec.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in spec.Arguments)
            startInfo.ArgumentList.Add(arg);
        startInfo.Environment["SERTO_PROTOCOL_VERSION"] = WireProtocol.Version;
        startInfo.Environment["SERTO_EXECUTION_ID"] = request.Metadata.ExecutionId.ToString();

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // Cancellation (timeout or agent shutdown) kills the process tree (and runs onCancel); the reads
        // below then unblock and we surface cancellation via ct.ThrowIfCancellationRequested.
        await using var killOnCancel = ct.Register(() =>
        {
            TryKill(process);
            try { onCancel?.Invoke(); } catch { /* best effort */ }
        });

        // Write stdin and read stdout concurrently. Doing them in sequence risks a deadlock: a chatty
        // integration could fill its stdout pipe (blocking it) before we finish writing its stdin.
        var writeStdin = WriteInvocationAsync(process, invocationJson, ct);
        var readStderr = ReadBoundedAsync(process.StandardError, ct);

        var result = await ReadEventsAsync(process, request, logger, ct);

        await writeStdin;
        await process.WaitForExitAsync(ct);
        var stderr = await readStderr;

        // Cancellation outranks any exit code: let the shared lifecycle classify it as timeout/shutdown.
        ct.ThrowIfCancellationRequested();

        if (result is { Succeeded: false })
            throw new IntegrationRunException(result.Error ?? "Integration reported failure.");

        if (process.ExitCode != 0 && result is null)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? "" : $" stderr: {stderr.Trim()}";
            throw new IntegrationRunException(
                $"Integration process exited with code {process.ExitCode}.{detail}");
        }

        // Exit 0 with no failure result → success: returning normally reports success.
    }

    // Streams stdout events, applying each to the run's logger/publisher, and returns the terminal result
    // event if the integration emitted one.
    private static async Task<WireResult?> ReadEventsAsync(
        Process process, RunRequest request, ILogger logger, CancellationToken ct)
    {
        WireResult? result = null;

        string? line;
        while ((line = await process.StandardOutput.ReadLineAsync(ct)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            WireEvent? evt;
            try
            {
                evt = JsonSerializer.Deserialize<WireEvent>(line, WireProtocol.Json);
            }
            catch (JsonException)
            {
                // Not a protocol line (a stray print, framework noise). Ignore — stderr captures real
                // diagnostics and the contract reserves stdout for JSON-lines.
                logger.LogDebug("Ignoring non-protocol stdout line from integration process");
                continue;
            }

            if (evt is null)
                continue;

            switch (evt.Type)
            {
                case "log":
                    EmitLog(request, evt);
                    break;
                case "message" when !string.IsNullOrEmpty(evt.Subject):
                    await request.Publisher.PublishRawAsync(evt.Subject, evt.Body, ct);
                    break;
                case "result":
                    result = new WireResult(evt.Succeeded ?? true, evt.Error);
                    break;
                default:
                    logger.LogDebug("Ignoring unknown wire event type '{Type}'", evt.Type);
                    break;
            }
        }

        return result;
    }

    private static void EmitLog(RunRequest request, WireEvent evt)
    {
        var level = Enum.TryParse<LogLevel>(evt.Level, ignoreCase: true, out var parsed)
            ? parsed
            : LogLevel.Information;

        // Fold any exception text into the message: the wire exception is a plain string, and the
        // execution logger only captures a real Exception object, so this keeps the detail visible.
        var message = string.IsNullOrEmpty(evt.Exception)
            ? evt.Message ?? ""
            : $"{evt.Message}\n{evt.Exception}";

        // "{Message}" template (not the raw string) so integration content with braces is never
        // interpreted as a format placeholder.
        request.IntegrationLogger.Log(level, "{Message}", message);
    }

    private static WireInvocation BuildInvocation(RunRequest request)
    {
        var i = request.Integration;
        var m = request.Metadata;

        return new WireInvocation(
            ProtocolVersion: WireProtocol.Version,
            Entrypoint: i.ClassName,
            Execution: new WireExecution(
                m.ExecutionId, m.IntegrationId, m.IntegrationName, m.Environment, m.ScheduledAt),
            Trigger: new WireTrigger(
                Type: i.TriggerType.ToLowerInvariant(),
                Cron: i.CronExpression,
                Subject: i.MessageSubject,
                DeliveryId: i.DeliveryId,
                MessageId: i.MessageId,
                PublishedAt: i.MessagePublishedAt),
            Payload: i.Payload,
            Secrets: request.Secrets);
    }

    private static async Task WriteInvocationAsync(Process process, string json, CancellationToken ct)
    {
        try
        {
            await process.StandardInput.WriteAsync(json.AsMemory(), ct);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The process may have exited before reading stdin; the exit code/stderr below report it.
        }
        catch (OperationCanceledException)
        {
            // Cancellation is surfaced by the caller after the reads unblock.
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            var text = await reader.ReadToEndAsync(ct);
            return text.Length > MaxStderrChars ? text[^MaxStderrChars..] : text;
        }
        catch (OperationCanceledException)
        {
            return "";
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already exited or not killable — nothing useful to do.
        }
    }

    private sealed record WireResult(bool Succeeded, string? Error);
}
