using RuntimeAgent.Agent;
using Serto.Sdk;

namespace RuntimeAgent.Execution;

// Runs .NET integrations in the agent's own process: loads the package assembly (or the agent's local
// integrations), resolves the IIntegration type by class name, and invokes RunAsync directly. This is
// the original execution path, now behind the runner seam. It is the fast path — no process or container
// overhead — and stays the default runtime.
public sealed class InProcessDotNetRunner(
    IntegrationLoader loader,
    IHttpClientFactory httpClientFactory,
    AgentOptions options) : IIntegrationRunner
{
    // No declared runtime (or an explicit "dotnet") means an in-process .NET assembly. Treat the absent
    // value as dotnet so existing integrations — which carry no runtime — keep running unchanged.
    public bool CanRun(IntegrationItem integration) =>
        string.IsNullOrEmpty(integration.Runtime) ||
        integration.Runtime.Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    public PreparedExecution? Prepare(IntegrationItem integration)
    {
        IIntegration? instance;
        if (integration.PackageId.HasValue)
        {
            var packageDir = Path.Combine(options.PackagesPath, integration.PackageId.Value.ToString());
            instance = loader.ResolveFromDirectory(integration.ClassName, packageDir);
        }
        else
        {
            instance = loader.Resolve(integration.ClassName);
        }

        return instance is null ? null : new Prepared(instance, httpClientFactory);
    }

    private sealed class Prepared(IIntegration instance, IHttpClientFactory httpClientFactory) : PreparedExecution
    {
        public override Task RunAsync(RunRequest request, CancellationToken ct)
        {
            var http = httpClientFactory.CreateClient("integration");
            var context = new ExecutionContext(
                request.Secrets,
                request.IntegrationLogger,
                http,
                request.Metadata,
                request.Publisher,
                request.Trigger,
                request.Integration.Payload);

            return instance.RunAsync(context, ct);
        }
    }
}
