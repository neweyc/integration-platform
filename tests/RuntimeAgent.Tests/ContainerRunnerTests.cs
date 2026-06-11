using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RuntimeAgent.Agent;
using RuntimeAgent.Execution;

namespace RuntimeAgent.Tests;

public class ContainerRunnerTests
{
    [Fact]
    public void CanRun_OnlyClaimsContainerRuntime()
    {
        var runner = new ContainerRunner(new AgentOptions(), NullLogger<ContainerRunner>.Instance);

        Assert.True(runner.CanRun(Item("container")));
        Assert.True(runner.CanRun(Item("Container")));
        Assert.False(runner.CanRun(Item("python")));
        Assert.False(runner.CanRun(Item("dotnet")));
        Assert.False(runner.CanRun(Item(null)));
    }

    [Fact]
    public void Prepare_NoImageReference_ReturnsNull()
    {
        var runner = new ContainerRunner(new AgentOptions(), NullLogger<ContainerRunner>.Instance);

        Assert.Null(runner.Prepare(Item("container", image: "")));
    }

    // Real end-to-end: build a tiny image whose entrypoint speaks the wire protocol, then run it through
    // the actual ContainerRunner + IntegrationExecutor. No-ops cleanly when docker isn't usable.
    [Fact]
    public async Task ContainerIntegration_RunsThroughContainerRunner_EndToEnd()
    {
        if (!Docker(["ps"], 15_000))
            return; // docker daemon not usable on this host — treated as skipped.

        var contextDir = Path.Combine(Path.GetTempPath(), "serto-ctr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contextDir);
        await File.WriteAllTextAsync(Path.Combine(contextDir, "harness.sh"),
            "cat > /dev/null\n" +
            "echo '{\"type\":\"log\",\"level\":\"Information\",\"message\":\"hello from container\"}'\n" +
            "echo '{\"type\":\"message\",\"subject\":\"container.greeted\",\"body\":\"{\\\"ok\\\":true}\"}'\n" +
            "echo '{\"type\":\"result\",\"succeeded\":true}'\n");
        await File.WriteAllTextAsync(Path.Combine(contextDir, "Dockerfile"),
            "FROM alpine\nCOPY harness.sh /harness.sh\nENTRYPOINT [\"/bin/sh\",\"/harness.sh\"]\n");

        var imageTag = "serto-itest-" + Guid.NewGuid().ToString("N");
        if (!Docker(["build", "-t", imageTag, contextDir], 180_000))
        {
            try { Directory.Delete(contextDir, recursive: true); } catch { /* best effort */ }
            return; // build failed (e.g. no network for the base image) — treated as skipped.
        }

        try
        {
            var executionId = Guid.NewGuid();
            var controlPlane = Substitute.For<IControlPlaneClient>();
            controlPlane.StartExecutionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(executionId);

            var options = new AgentOptions { Environment = "production" };
            var runner = new ContainerRunner(options, NullLogger<ContainerRunner>.Instance);
            var executor = new IntegrationExecutor(
                controlPlane, [runner], options, NullLogger<IntegrationExecutor>.Instance);

            var integration = new IntegrationItem(
                Guid.NewGuid(), "Container Integration", "container-integration",
                "Scheduled", "0 * * * *", imageTag,
                DateTime.UtcNow.AddMinutes(5), "Scheduled", null,
                WorkItemId: Guid.NewGuid(),
                Runtime: "container");

            await executor.ExecuteAsync(integration, new Dictionary<string, string>(), CancellationToken.None);

            await controlPlane.Received(1).CompleteExecutionAsync(
                executionId, succeeded: true, errorMessage: null, Arg.Any<CancellationToken>());
            await controlPlane.Received().RecordLogAsync(
                executionId,
                Arg.Is<ExecutionLogEntry>(l => l.Message.Contains("hello from container")),
                Arg.Any<CancellationToken>());
            await controlPlane.Received(1).PublishMessageAsync(
                "container.greeted",
                Arg.Is<string?>(body => body != null && body.Contains("ok")),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            Docker(["rmi", "-f", imageTag], 60_000);
            try { Directory.Delete(contextDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static IntegrationItem Item(string? runtime, string image = "alpine") =>
        new(Guid.NewGuid(), "x", "x", "Scheduled", null, image, null, "Scheduled", null, Runtime: runtime);

    private static bool Docker(string[] args, int timeoutMs)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var arg in args)
                startInfo.ArgumentList.Add(arg);

            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
