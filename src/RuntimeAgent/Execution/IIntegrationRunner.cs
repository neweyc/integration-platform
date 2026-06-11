using Microsoft.Extensions.Logging;
using RuntimeAgent.Agent;
using Serto.Sdk;

namespace RuntimeAgent.Execution;

// A runtime adapter: knows how to execute one integration once everything around the run has been
// prepared. The surrounding lifecycle — claiming the execution, building the logger/publisher/trigger,
// enforcing the timeout, flushing logs, reporting completion, and mapping failures — lives in
// IntegrationExecutor and is identical for every runtime. Adding a new language means adding a runner,
// not touching that lifecycle.
//
// Today the only implementation is InProcessDotNetRunner (loads a .NET assembly and calls RunAsync in
// the agent's own process). Subprocess and container runners slot in beside it, selected by the
// integration's declared Runtime.
public interface IIntegrationRunner
{
    // Whether this runner handles the given work item, based on its declared runtime.
    bool CanRun(IntegrationItem integration);

    // Resolves the integration into a ready-to-run handle, or null if it isn't available yet (e.g. its
    // package hasn't finished syncing to disk). Returning null tells the caller to skip and retry later
    // WITHOUT starting an execution, so a still-syncing package never burns a failed run/retry attempt.
    PreparedExecution? Prepare(IntegrationItem integration);
}

// A resolved integration, ready to execute. RunAsync performs the actual work given the runtime-neutral
// RunRequest; surfacing logs and published messages happens through the request's logger and publisher,
// and failure is signalled by throwing (including OperationCanceledException on timeout/shutdown) so the
// shared lifecycle can map every runtime's outcome the same way.
public abstract class PreparedExecution
{
    public abstract Task RunAsync(RunRequest request, CancellationToken ct);
}

// Everything a runner needs that the shared lifecycle has already prepared. Runtime-neutral: the
// in-process runner turns this into an IIntegrationContext object; a subprocess/container runner would
// serialize the same data onto its input channel.
public sealed record RunRequest(
    IntegrationItem Integration,
    IReadOnlyDictionary<string, string> Secrets,
    ExecutionMetadata Metadata,
    ILogger IntegrationLogger,
    IExecutionMessagePublisher Publisher,
    TriggerInfo Trigger);
