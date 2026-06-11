using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RuntimeAgent.Agent;
using RuntimeAgent.Execution;

namespace RuntimeAgent.Tests;

// Exercises the SubprocessRunner end-to-end through IntegrationExecutor, driving a real child process via
// /bin/sh so no Python/Node toolchain is required. The shell scripts stand in for a language SDK's
// harness: they consume the invocation on stdin and emit wire-protocol JSON-lines on stdout.
public class SubprocessRunnerTests
{
    [Fact]
    public async Task SuccessfulRun_ForwardsLogs_AndReportsSuccess()
    {
        const string script = """
            cat > /dev/null
            echo '{"type":"log","level":"Information","message":"hello from script"}'
            echo '{"type":"result","succeeded":true}'
            """;

        await RunScript(script, async (executor, integration, controlPlane, executionId) =>
        {
            await executor.ExecuteAsync(integration, new Dictionary<string, string>(), CancellationToken.None);

            await controlPlane.Received(1).CompleteExecutionAsync(
                executionId, succeeded: true, errorMessage: null, Arg.Any<CancellationToken>());
            await controlPlane.Received().RecordLogAsync(
                executionId,
                Arg.Is<ExecutionLogEntry>(l => l.Message.Contains("hello from script")),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task PublishedMessage_IsForwardedVerbatim()
    {
        const string script = """
            cat > /dev/null
            echo '{"type":"message","subject":"orders.created","body":"{\"id\":1}"}'
            echo '{"type":"result","succeeded":true}'
            """;

        await RunScript(script, async (executor, integration, controlPlane, _) =>
        {
            await executor.ExecuteAsync(integration, new Dictionary<string, string>(), CancellationToken.None);

            await controlPlane.Received(1).PublishMessageAsync(
                "orders.created",
                Arg.Is<string?>(body => body != null && body.Contains("\"id\":1")),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ResultReportsFailure_ReportsFailureWithError()
    {
        const string script = """
            cat > /dev/null
            echo '{"type":"result","succeeded":false,"error":"boom from script"}'
            """;

        await RunScript(script, async (executor, integration, controlPlane, executionId) =>
        {
            await executor.ExecuteAsync(integration, new Dictionary<string, string>(), CancellationToken.None);

            await controlPlane.Received(1).CompleteExecutionAsync(
                executionId, succeeded: false,
                Arg.Is<string>(msg => msg.Contains("boom from script")),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task NonZeroExitWithoutResult_ReportsFailureWithStderr()
    {
        const string script = """
            cat > /dev/null
            echo 'something broke' 1>&2
            exit 3
            """;

        await RunScript(script, async (executor, integration, controlPlane, executionId) =>
        {
            await executor.ExecuteAsync(integration, new Dictionary<string, string>(), CancellationToken.None);

            await controlPlane.Received(1).CompleteExecutionAsync(
                executionId, succeeded: false,
                Arg.Is<string>(msg => msg.Contains("exited with code 3") && msg.Contains("something broke")),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ExceedingTimeout_ReportsTimedOut()
    {
        const string script = """
            cat > /dev/null
            sleep 30
            """;

        await RunScript(script, async (executor, integration, controlPlane, executionId) =>
        {
            await executor.ExecuteAsync(integration, new Dictionary<string, string>(), CancellationToken.None);

            await controlPlane.Received(1).CompleteExecutionAsync(
                executionId, succeeded: false,
                Arg.Is<string?>(msg => msg != null && msg.Contains("timed out")),
                Arg.Any<CancellationToken>(),
                isTimeout: true);
        }, timeoutSeconds: 1);
    }

    // Writes the script to a temp working directory, wires a SubprocessRunner behind IntegrationExecutor
    // pointed at /bin/sh, runs the caller's assertions, then cleans up.
    private static async Task RunScript(
        string scriptBody,
        Func<IntegrationExecutor, IntegrationItem, IControlPlaneClient, Guid, Task> assert,
        int? timeoutSeconds = null)
    {
        var workingDir = Path.Combine(Path.GetTempPath(), "serto-subproc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDir);
        var scriptPath = Path.Combine(workingDir, "run.sh");
        await File.WriteAllTextAsync(scriptPath, scriptBody.ReplaceLineEndings("\n"));

        try
        {
            var executionId = Guid.NewGuid();
            var controlPlane = Substitute.For<IControlPlaneClient>();
            controlPlane.StartExecutionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(executionId);

            var options = new AgentOptions { Environment = "production", IntegrationsPath = workingDir };
            var runner = new SubprocessRunner(
                new ShellScriptResolver(scriptPath), options, NullLogger<SubprocessRunner>.Instance);
            var executor = new IntegrationExecutor(
                controlPlane, [runner], options, NullLogger<IntegrationExecutor>.Instance);

            var integration = new IntegrationItem(
                Guid.NewGuid(), "Script Integration", "script-integration",
                "Scheduled", "0 * * * *", "run.sh:handler",
                DateTime.UtcNow.AddMinutes(5), "Scheduled", null,
                TimeoutSeconds: timeoutSeconds,
                WorkItemId: Guid.NewGuid(),
                Runtime: "script");

            await assert(executor, integration, controlPlane, executionId);
        }
        finally
        {
            try { Directory.Delete(workingDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // Stand-in launch resolver: every "script" runtime runs the prepared shell script under /bin/sh.
    private sealed class ShellScriptResolver(string scriptPath) : IRuntimeLaunchResolver
    {
        public bool Supports(string? runtime) => runtime == "script";

        public RuntimeLaunchSpec? Resolve(string runtime, string workingDirectory) =>
            new("/bin/sh", [scriptPath], workingDirectory);
    }
}
