using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RuntimeAgent.Agent;
using RuntimeAgent.Execution;

namespace RuntimeAgent.Tests;

// Exercises the ShellRunner end-to-end through IntegrationExecutor, running real commands via /bin/sh.
// The "entrypoint" is a raw command line — no SDK, no wire protocol — so these confirm the bring-your-
// own-script path: output captured as logs, secrets delivered as env vars, exit code mapped to outcome.
public class ShellRunnerTests
{
    [Fact]
    public async Task SuccessfulCommand_CapturesStdout_AndReportsSuccess()
    {
        await Run("echo 'hello from shell'", async (executor, integration, controlPlane, executionId) =>
        {
            await executor.ExecuteAsync(integration, new Dictionary<string, string>(), CancellationToken.None);

            await controlPlane.Received(1).CompleteExecutionAsync(
                executionId, succeeded: true, errorMessage: null, Arg.Any<CancellationToken>());
            await controlPlane.Received().RecordLogAsync(
                executionId,
                Arg.Is<ExecutionLogEntry>(l => l.Message.Contains("hello from shell")),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task NonZeroExit_ReportsFailureWithStderrTail()
    {
        await Run("echo 'something broke' 1>&2; exit 7", async (executor, integration, controlPlane, executionId) =>
        {
            await executor.ExecuteAsync(integration, new Dictionary<string, string>(), CancellationToken.None);

            await controlPlane.Received(1).CompleteExecutionAsync(
                executionId, succeeded: false,
                Arg.Is<string>(msg => msg.Contains("exited with code 7") && msg.Contains("something broke")),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Secrets_AreExposedAsEnvironmentVariables()
    {
        await Run("echo \"key=$API_KEY\"", async (executor, integration, controlPlane, executionId) =>
        {
            await executor.ExecuteAsync(
                integration,
                new Dictionary<string, string> { ["API_KEY"] = "sk-secret-123" },
                CancellationToken.None);

            await controlPlane.Received().RecordLogAsync(
                executionId,
                Arg.Is<ExecutionLogEntry>(l => l.Message.Contains("key=sk-secret-123")),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ExecutionMetadata_IsExposedAsEnvironmentVariables()
    {
        await Run("echo \"env=$SERTO_ENVIRONMENT trigger=$SERTO_TRIGGER_TYPE\"",
            async (executor, integration, controlPlane, executionId) =>
            {
                await executor.ExecuteAsync(integration, new Dictionary<string, string>(), CancellationToken.None);

                await controlPlane.Received().RecordLogAsync(
                    executionId,
                    Arg.Is<ExecutionLogEntry>(l => l.Message.Contains("env=production") && l.Message.Contains("trigger=scheduled")),
                    Arg.Any<CancellationToken>());
            });
    }

    [Fact]
    public async Task ExceedingTimeout_ReportsTimedOut()
    {
        await Run("sleep 30", async (executor, integration, controlPlane, executionId) =>
        {
            await executor.ExecuteAsync(integration, new Dictionary<string, string>(), CancellationToken.None);

            await controlPlane.Received(1).CompleteExecutionAsync(
                executionId, succeeded: false,
                Arg.Is<string?>(msg => msg != null && msg.Contains("timed out")),
                Arg.Any<CancellationToken>(),
                isTimeout: true);
        }, timeoutSeconds: 1);
    }

    private static async Task Run(
        string command,
        Func<IntegrationExecutor, IntegrationItem, IControlPlaneClient, Guid, Task> assert,
        int? timeoutSeconds = null)
    {
        var workingDir = Path.Combine(Path.GetTempPath(), "serto-shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDir);

        try
        {
            var executionId = Guid.NewGuid();
            var controlPlane = Substitute.For<IControlPlaneClient>();
            controlPlane.StartExecutionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(executionId);

            var options = new AgentOptions { Environment = "production", IntegrationsPath = workingDir };
            var runner = new ShellRunner(options, NullLogger<ShellRunner>.Instance);
            var executor = new IntegrationExecutor(
                controlPlane, [runner], options, NullLogger<IntegrationExecutor>.Instance);

            // The entrypoint (ClassName) is the raw command line; runtime "shell".
            var integration = new IntegrationItem(
                Guid.NewGuid(), "Shell Job", "shell-job",
                "Scheduled", "0 * * * *", command,
                DateTime.UtcNow.AddMinutes(5), "Scheduled", null,
                TimeoutSeconds: timeoutSeconds,
                WorkItemId: Guid.NewGuid(),
                Runtime: "shell");

            await assert(executor, integration, controlPlane, executionId);
        }
        finally
        {
            try { Directory.Delete(workingDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
