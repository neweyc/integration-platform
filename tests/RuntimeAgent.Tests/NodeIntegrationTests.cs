using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RuntimeAgent.Agent;
using RuntimeAgent.Execution;

namespace RuntimeAgent.Tests;

// The cross-language proof for Node: a real Node.js integration, launched via the serto harness, run
// through the actual SubprocessRunner and IntegrationExecutor. No-ops cleanly when node or the SDK
// harness isn't present.
public class NodeIntegrationTests
{
    [Fact]
    public async Task NodeIntegration_RunsThroughSubprocessRunner_EndToEnd()
    {
        var node = FindOnPath("node");
        if (node is null)
            return; // node unavailable — treated as skipped.

        var harness = LocateNodeHarness();
        if (harness is null)
            return; // SDK harness not found relative to the test output — treated as skipped.

        var workingDir = Path.Combine(Path.GetTempPath(), "serto-node-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDir);
        await File.WriteAllTextAsync(Path.Combine(workingDir, "index.js"), HandlerSource);

        try
        {
            var executionId = Guid.NewGuid();
            var controlPlane = Substitute.For<IControlPlaneClient>();
            controlPlane.StartExecutionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(executionId);

            var options = new AgentOptions { Environment = "production", IntegrationsPath = workingDir };
            var runner = new SubprocessRunner(
                new NodeResolver(node, harness), options, NullLogger<SubprocessRunner>.Instance);
            var executor = new IntegrationExecutor(
                controlPlane, [runner], options, NullLogger<IntegrationExecutor>.Instance);

            var integration = new IntegrationItem(
                Guid.NewGuid(), "Hello Node", "hello-node",
                "Scheduled", "0 * * * *", "index.js#handler",
                DateTime.UtcNow.AddMinutes(5), "Scheduled", null,
                WorkItemId: Guid.NewGuid(),
                Runtime: "node");

            await executor.ExecuteAsync(
                integration,
                new Dictionary<string, string> { ["API_KEY"] = "sk-node-99" },
                CancellationToken.None);

            await controlPlane.Received(1).CompleteExecutionAsync(
                executionId, succeeded: true, errorMessage: null, Arg.Any<CancellationToken>());
            await controlPlane.Received().RecordLogAsync(
                executionId,
                Arg.Is<ExecutionLogEntry>(l => l.Message.Contains("Hello from Node")),
                Arg.Any<CancellationToken>());
            await controlPlane.Received(1).PublishMessageAsync(
                "node.greeted",
                Arg.Is<string?>(body => body != null && body.Contains("greeted")),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            try { Directory.Delete(workingDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private const string HandlerSource =
        "module.exports.handler = async (ctx) => {\n" +
        "  ctx.logger.info('Hello from Node');\n" +
        "  ctx.logger.info('has API_KEY: ' + ('API_KEY' in ctx.secrets));\n" +
        "  await ctx.publish('node.greeted', { greeted: true });\n" +
        "};\n";

    // Launches node with the SDK harness; the harness resolves index.js#handler relative to the working
    // directory and requires the SDK relative to its own location.
    private sealed class NodeResolver(string nodePath, string harnessPath) : IRuntimeLaunchResolver
    {
        public bool Supports(string? runtime) => runtime == "node";

        public RuntimeLaunchSpec? Resolve(string runtime, string workingDirectory) =>
            new(nodePath, [harnessPath], workingDirectory);
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

    // Walks up from the test output directory to find the Node SDK harness.
    private static string? LocateNodeHarness()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "sdks", "node", "serto", "bin", "serto-runtime.js");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
