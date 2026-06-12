using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using RuntimeAgent.Agent;

namespace RuntimeAgent.Execution;

// Runs a raw command or script — no Serto SDK, no wire protocol. The integration's "entrypoint" is a
// command line (e.g. "./nightly-close.sh" or "sqlplus -s user/$DB_PW@orcl @close.sql") run through a
// shell. Inputs arrive as ENVIRONMENT VARIABLES (secrets under their own names, plus SERTO_* metadata),
// all stdout/stderr is captured as logs, and the process exit code decides the outcome (0 = success,
// non-zero = failure).
//
// This is the bring-your-existing-scripts path: get scheduling, secrets, logs, retries, and alerts around
// the scripts you already run under cron / Control-M / EBS, without rewriting them. Trade-offs vs the SDK
// path: secrets are delivered as env vars (visible to the process, as every script runner does), and the
// agent host must have whatever the script needs (a shell, sqlplus, …) — or run it in a container image.
public sealed class ShellRunner(AgentOptions options, ILogger<ShellRunner> logger) : IIntegrationRunner
{
    // Maximum stderr characters retained for the failure message on a non-zero exit (all of it is logged
    // regardless; this is just the tail folded into the error).
    private const int MaxStderrTailChars = 4000;

    public static bool IsShellRuntime(string? runtime) =>
        string.Equals(runtime, "shell", StringComparison.OrdinalIgnoreCase);

    public bool CanRun(IntegrationItem integration) => IsShellRuntime(integration.Runtime);

    public PreparedExecution? Prepare(IntegrationItem integration)
    {
        var command = integration.ClassName?.Trim();
        if (string.IsNullOrEmpty(command))
        {
            logger.LogWarning("Skipping {Name}: shell integration has no command (entrypoint).", integration.Name);
            return null;
        }

        var workingDirectory = integration.PackageId.HasValue
            ? Path.Combine(options.PackagesPath, integration.PackageId.Value.ToString())
            : options.IntegrationsPath;

        // A pinned package whose directory hasn't synced yet: skip and retry, like the other runners.
        if (integration.PackageId.HasValue && !Directory.Exists(workingDirectory))
        {
            logger.LogWarning(
                "Package {PackageId} for {Name} not synced yet — skipping until available",
                integration.PackageId, integration.Name);
            return null;
        }

        return new Prepared(options.Shell, command, Path.GetFullPath(workingDirectory));
    }

    private sealed class Prepared(ShellOptions shell, string command, string workingDirectory) : PreparedExecution
    {
        public override async Task RunAsync(RunRequest request, CancellationToken ct)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = shell.Executable,
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var arg in shell.Args)
                startInfo.ArgumentList.Add(arg);
            startInfo.ArgumentList.Add(command);
            ApplyEnvironment(startInfo, request);

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            process.StandardInput.Close(); // raw jobs get no stdin — a deterministic EOF rather than a hang

            await using var killOnCancel = ct.Register(() => TryKill(process));

            // Stream both pipes to the execution log as they arrive. stderr is also kept (bounded) so a
            // non-zero exit can report a useful reason. stderr is logged at Warning for visibility only — it
            // does NOT imply failure; the exit code is the sole source of truth for pass/fail.
            var readStdout = ForwardAsync(process.StandardOutput, request, LogLevel.Information, keepTail: false, ct);
            var readStderr = ForwardAsync(process.StandardError, request, LogLevel.Warning, keepTail: true, ct);

            await Task.WhenAll(readStdout, readStderr);
            await process.WaitForExitAsync(ct);

            // Cancellation outranks the exit code: let the shared lifecycle classify timeout/shutdown.
            ct.ThrowIfCancellationRequested();

            if (process.ExitCode != 0)
            {
                var tail = (await readStderr).Trim();
                var detail = string.IsNullOrEmpty(tail) ? "" : $" stderr: {tail}";
                throw new IntegrationRunException($"Script exited with code {process.ExitCode}.{detail}");
            }

            // Exit 0 → success: returning normally reports success.
        }

        // Delivers inputs the way scripts expect them: secrets as environment variables under their own
        // names, plus SERTO_* execution/trigger metadata and the raw payload. Secret values override any
        // inherited env var of the same name (the secret wins).
        private static void ApplyEnvironment(ProcessStartInfo startInfo, RunRequest request)
        {
            foreach (var secret in request.Secrets)
                startInfo.Environment[secret.Key] = secret.Value;

            var m = request.Metadata;
            startInfo.Environment["SERTO_EXECUTION_ID"] = m.ExecutionId.ToString();
            startInfo.Environment["SERTO_INTEGRATION_NAME"] = m.IntegrationName;
            startInfo.Environment["SERTO_ENVIRONMENT"] = m.Environment;
            startInfo.Environment["SERTO_SCHEDULED_AT"] = m.ScheduledAt.ToString("O");
            startInfo.Environment["SERTO_TRIGGER_TYPE"] = request.Integration.TriggerType.ToLowerInvariant();

            if (!string.IsNullOrEmpty(request.Integration.MessageSubject))
                startInfo.Environment["SERTO_MESSAGE_SUBJECT"] = request.Integration.MessageSubject;
            if (request.Integration.Payload is not null)
                startInfo.Environment["SERTO_PAYLOAD"] = request.Integration.Payload;
        }

        // Logs every line from a stream and, when keepTail is set, retains a bounded tail for the failure
        // message.
        private static async Task<string> ForwardAsync(
            StreamReader reader, RunRequest request, LogLevel level, bool keepTail, CancellationToken ct)
        {
            var tail = keepTail ? new StringBuilder() : null;

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                // "{Line}" template (not the raw string) so script output containing braces is never
                // interpreted as a log format placeholder.
                request.IntegrationLogger.Log(level, "{Line}", line);

                if (tail is not null)
                {
                    tail.Append(line).Append('\n');
                    if (tail.Length > MaxStderrTailChars)
                        tail.Remove(0, tail.Length - MaxStderrTailChars);
                }
            }

            return tail?.ToString() ?? "";
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
    }
}
