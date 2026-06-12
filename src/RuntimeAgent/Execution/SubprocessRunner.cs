using Microsoft.Extensions.Logging;
using RuntimeAgent.Agent;
using Shared.Manifest;

namespace RuntimeAgent.Execution;

// Runs an integration as a host child process (python, node, a compiled binary, …) and speaks the wire
// protocol with it via WireProtocolHost. Selected for any non-.NET, non-container runtime the agent has a
// launch command for. The surrounding lifecycle — claiming the execution, timeout, completion reporting —
// is unchanged; this runner only resolves how to launch the process.
public sealed class SubprocessRunner(
    IRuntimeLaunchResolver launchResolver,
    AgentOptions options,
    ILogger<SubprocessRunner> logger) : IIntegrationRunner
{
    public bool CanRun(IntegrationItem integration) =>
        !Runtimes.IsDotnet(integration.Runtime)
        && !ContainerRunner.IsContainerRuntime(integration.Runtime)
        && !ShellRunner.IsShellRuntime(integration.Runtime)
        && launchResolver.Supports(integration.Runtime);

    public PreparedExecution? Prepare(IntegrationItem integration)
    {
        var workingDirectory = integration.PackageId.HasValue
            ? Path.Combine(options.PackagesPath, integration.PackageId.Value.ToString())
            : options.IntegrationsPath;

        // A pinned package whose directory hasn't synced yet: skip and retry, exactly like the in-process
        // runner, rather than launching against a missing directory.
        if (integration.PackageId.HasValue && !Directory.Exists(workingDirectory))
        {
            logger.LogWarning(
                "Package {PackageId} for {Name} not synced yet — skipping until available",
                integration.PackageId, integration.Name);
            return null;
        }

        var spec = launchResolver.Resolve(integration.Runtime!, Path.GetFullPath(workingDirectory));
        if (spec is null)
        {
            logger.LogWarning(
                "No launch configuration for runtime '{Runtime}' — skipping {Name}",
                integration.Runtime, integration.Name);
            return null;
        }

        return new Prepared(spec, logger);
    }

    private sealed class Prepared(RuntimeLaunchSpec spec, ILogger logger) : PreparedExecution
    {
        public override Task RunAsync(RunRequest request, CancellationToken ct) =>
            WireProtocolHost.RunAsync(spec, request, logger, ct);
    }
}
