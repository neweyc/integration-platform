using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RuntimeAgent.Agent;

namespace RuntimeAgent.Execution;

// Runs an integration as a container — `docker run --rm -i <image>` — speaking the same wire protocol over
// the container's stdin/stdout via WireProtocolHost. The image is self-contained: it carries the
// integration and its language harness, so build and platform concerns live inside the image rather than
// on the agent. That makes this the clean host for compiled runtimes (Go, Rust, …) and the strong
// isolation story.
//
// The image reference is the integration's entrypoint (e.g. "ghcr.io/acme/sync:1.0"). Secrets travel in
// the stdin invocation, not env vars, so nothing sensitive appears in `docker inspect` or the host
// process table.
public sealed class ContainerRunner(AgentOptions options, ILogger<ContainerRunner> logger) : IIntegrationRunner
{
    public static bool IsContainerRuntime(string? runtime) =>
        string.Equals(runtime, "container", StringComparison.OrdinalIgnoreCase);

    public bool CanRun(IntegrationItem integration) => IsContainerRuntime(integration.Runtime);

    public PreparedExecution? Prepare(IntegrationItem integration)
    {
        var image = integration.ClassName?.Trim();
        if (string.IsNullOrEmpty(image))
        {
            logger.LogWarning(
                "Skipping {Name}: container integration has no image reference (entrypoint).", integration.Name);
            return null;
        }

        return new Prepared(options.Container, image, logger);
    }

    private sealed class Prepared(ContainerOptions container, string image, ILogger logger) : PreparedExecution
    {
        public override Task RunAsync(RunRequest request, CancellationToken ct)
        {
            // A unique, --rm container name lets us stop exactly this run on timeout/shutdown — killing the
            // docker client process alone would leave the container running.
            var containerName = $"serto-{request.Metadata.ExecutionId:N}";

            var args = new List<string>(container.RunArgs) { "--name", containerName, image };
            var spec = new RuntimeLaunchSpec(container.Engine, args, WorkingDirectory: Path.GetTempPath());

            return WireProtocolHost.RunAsync(
                spec, request, logger, ct, onCancel: () => StopContainer(container.Engine, containerName));
        }

        // Best-effort stop of the running container when the run is cancelled. Fire-and-forget: the run is
        // already being torn down, and a failure here must not mask the cancellation.
        private static void StopContainer(string engine, string containerName)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = engine,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add("kill");
                startInfo.ArgumentList.Add(containerName);
                Process.Start(startInfo);
            }
            catch
            {
                // Engine missing or already gone — nothing useful to do.
            }
        }
    }
}
