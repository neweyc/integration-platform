using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RuntimeAgent.Agent;
using RuntimeAgent.Execution;

namespace RuntimeAgent.Tests;

// The cross-language proof: a real Python integration, launched as python3 -m serto, run through the
// actual SubprocessRunner and IntegrationExecutor. Confirms the Python SDK's wire format and the agent's
// parser agree end to end. No-ops cleanly when python3 isn't installed (xUnit 2.x has no runtime skip).
public class PythonIntegrationTests
{
    [Fact]
    public async Task PythonIntegration_RunsThroughSubprocessRunner_EndToEnd()
    {
        var python = FindOnPath("python3");
        if (python is null)
            return; // python3 unavailable on this host — treated as skipped.

        var sdkDir = LocatePythonSdkDir();
        if (sdkDir is null)
            return; // SDK sources not found relative to the test output — treated as skipped.

        var workingDir = Path.Combine(Path.GetTempPath(), "serto-py-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDir);
        await File.WriteAllTextAsync(Path.Combine(workingDir, "main.py"), HandlerSource);

        try
        {
            var executionId = Guid.NewGuid();
            var controlPlane = Substitute.For<IControlPlaneClient>();
            controlPlane.StartExecutionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(executionId);

            var options = new AgentOptions { Environment = "production", IntegrationsPath = workingDir };
            var runner = new SubprocessRunner(
                new PythonResolver(python, sdkDir), options, NullLogger<SubprocessRunner>.Instance);
            var executor = new IntegrationExecutor(
                controlPlane, [runner], options, NullLogger<IntegrationExecutor>.Instance);

            var integration = new IntegrationItem(
                Guid.NewGuid(), "Hello Python", "hello-python",
                "Scheduled", "0 * * * *", "main.py:handler",
                DateTime.UtcNow.AddMinutes(5), "Scheduled", null,
                WorkItemId: Guid.NewGuid(),
                Runtime: "python");

            await executor.ExecuteAsync(
                integration,
                new Dictionary<string, string> { ["API_KEY"] = "sk-test-12345" },
                CancellationToken.None);

            // The Python handler logged a greeting and published a message, then the run succeeded.
            await controlPlane.Received(1).CompleteExecutionAsync(
                executionId, succeeded: true, errorMessage: null, Arg.Any<CancellationToken>());
            await controlPlane.Received().RecordLogAsync(
                executionId,
                Arg.Is<ExecutionLogEntry>(l => l.Message.Contains("Hello from Python")),
                Arg.Any<CancellationToken>());
            await controlPlane.Received(1).PublishMessageAsync(
                "python.greeted",
                Arg.Is<string?>(body => body != null && body.Contains("greeted")),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            try { Directory.Delete(workingDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // A self-contained Python integration written into the working directory for the test.
    private const string HandlerSource =
        "def handler(ctx):\n" +
        "    ctx.logger.info('Hello from Python')\n" +
        "    ctx.logger.info('has API_KEY: %s' % ('API_KEY' in ctx.secrets))\n" +
        "    ctx.publish('python.greeted', {'greeted': True})\n";

    // Launches python3 with the SDK injected on sys.path (no env mutation), running the serto harness.
    private sealed class PythonResolver(string pythonPath, string sdkDir) : IRuntimeLaunchResolver
    {
        public bool Supports(string? runtime) => runtime == "python";

        public RuntimeLaunchSpec? Resolve(string runtime, string workingDirectory)
        {
            var bootstrap =
                $"import sys; sys.path.insert(0, r'{sdkDir}'); from serto._harness import main; main()";
            return new RuntimeLaunchSpec(pythonPath, ["-c", bootstrap], workingDirectory);
        }
    }

    private static string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
            return null;

        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            var candidate = Path.Combine(dir, executable);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    // Walks up from the test output directory to find sdks/python (the dir containing the serto package).
    private static string? LocatePythonSdkDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "sdks", "python", "serto", "__main__.py");
            if (File.Exists(candidate))
                return Path.Combine(dir.FullName, "sdks", "python");
            dir = dir.Parent;
        }
        return null;
    }
}
