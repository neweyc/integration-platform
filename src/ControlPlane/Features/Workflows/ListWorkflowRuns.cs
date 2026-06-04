using ControlPlane.Infrastructure;

namespace ControlPlane.Features.Workflows;

public record ListWorkflowRunsCommand(Guid TenantId, Guid WorkflowId, int Limit) : ICommand<ListWorkflowRunsResult>;

public record ListWorkflowRunsResult(IReadOnlyList<WorkflowRunResult> Runs);

public class ListWorkflowRunsHandler(IWorkflowRepository repository)
    : ICommandHandler<ListWorkflowRunsCommand, ListWorkflowRunsResult>
{
    private const int MaxLimit = 100;

    public async Task<ListWorkflowRunsResult> HandleAsync(ListWorkflowRunsCommand command, CancellationToken ct = default)
    {
        var limit = Math.Clamp(command.Limit, 1, MaxLimit);
        var runs = await repository.ListRunsAsync(command.TenantId, command.WorkflowId, limit, ct);
        return new ListWorkflowRunsResult(runs.Select(WorkflowMapping.ToResult).ToList());
    }
}
