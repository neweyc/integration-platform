using ControlPlane.Infrastructure;

namespace ControlPlane.Features.Workflows;

public record ListWorkflowsCommand(Guid TenantId, string? Environment) : ICommand<ListWorkflowsResult>;

public record ListWorkflowsResult(IReadOnlyList<WorkflowDefinitionResult> Workflows);

public class ListWorkflowsHandler(IWorkflowRepository repository)
    : ICommandHandler<ListWorkflowsCommand, ListWorkflowsResult>
{
    public async Task<ListWorkflowsResult> HandleAsync(ListWorkflowsCommand command, CancellationToken ct = default)
    {
        var workflows = await repository.ListDefinitionsAsync(command.TenantId, command.Environment, ct);
        return new ListWorkflowsResult(workflows.Select(WorkflowMapping.ToResult).ToList());
    }
}
