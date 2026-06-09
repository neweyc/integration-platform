using ControlPlane.Features.Environments;
using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Workflows;

public record CreateWorkflowCommand(
    Guid TenantId,
    string Name,
    string Slug,
    string Environment,
    IReadOnlyList<WorkflowNodeInput> Nodes,
    IReadOnlyList<WorkflowEdgeInput> Edges) : ICommand<WorkflowDefinitionResult>;

public class CreateWorkflowHandler(IWorkflowRepository repository, IEnvironmentReadRepository environments)
    : ICommandHandler<CreateWorkflowCommand, WorkflowDefinitionResult>
{
    public async Task<WorkflowDefinitionResult> HandleAsync(CreateWorkflowCommand command, CancellationToken ct = default)
    {
        ValidateShape(command);

        var environment = EnvironmentKey.Normalize(command.Environment);
        if (!await environments.ExistsAsync(command.TenantId, environment, ct))
            throw new ValidationException($"Environment '{environment}' does not exist. Create it before defining workflows in it.");

        if (await repository.SlugExistsAsync(command.TenantId, command.Slug, ct))
            throw new ConflictException($"A workflow with slug '{command.Slug}' already exists.");

        var integrationIds = command.Nodes.Select(n => n.IntegrationId).Distinct().ToList();
        var integrations = await repository.GetIntegrationsAsync(command.TenantId, integrationIds, ct);
        var integrationsById = integrations.ToDictionary(i => i.Id);

        foreach (var node in command.Nodes)
        {
            if (!integrationsById.TryGetValue(node.IntegrationId, out var integration))
                throw new NotFoundException($"Integration '{node.IntegrationId}' not found.");

            if (integration.Environment != environment)
                throw new ValidationException($"Integration '{integration.Id}' belongs to environment '{integration.Environment}', not '{environment}'.");
        }

        var workflow = new WorkflowDefinition
        {
            TenantId = command.TenantId,
            Name = command.Name.Trim(),
            Slug = command.Slug.Trim(),
            Environment = environment,
            Status = WorkflowStatus.Enabled
        };

        var nodesByKey = new Dictionary<string, WorkflowNode>(StringComparer.Ordinal);
        foreach (var input in command.Nodes)
        {
            var node = new WorkflowNode
            {
                TenantId = command.TenantId,
                WorkflowDefinitionId = workflow.Id,
                Key = input.Key.Trim(),
                Name = input.Name.Trim(),
                IntegrationId = input.IntegrationId
            };
            workflow.Nodes.Add(node);
            nodesByKey.Add(node.Key, node);
        }

        foreach (var edge in command.Edges)
        {
            workflow.Edges.Add(new WorkflowEdge
            {
                TenantId = command.TenantId,
                WorkflowDefinitionId = workflow.Id,
                FromNodeId = nodesByKey[edge.From].Id,
                ToNodeId = nodesByKey[edge.To].Id,
                FromNode = nodesByKey[edge.From],
                ToNode = nodesByKey[edge.To]
            });
        }

        var created = await repository.CreateAsync(workflow, ct);
        return WorkflowMapping.ToResult(created);
    }

    private static void ValidateShape(CreateWorkflowCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ValidationException("Name is required.");

        if (string.IsNullOrWhiteSpace(command.Slug))
            throw new ValidationException("Slug is required.");

        if (!System.Text.RegularExpressions.Regex.IsMatch(command.Slug, @"^[a-z0-9-]+$"))
            throw new ValidationException("Slug may only contain lowercase letters, numbers, and hyphens.");

        if (string.IsNullOrWhiteSpace(command.Environment))
            throw new ValidationException("Environment is required.");

        if (command.Nodes.Count == 0)
            throw new ValidationException("At least one workflow node is required.");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in command.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Key))
                throw new ValidationException("Node key is required.");

            if (!System.Text.RegularExpressions.Regex.IsMatch(node.Key, @"^[a-zA-Z0-9_-]+$"))
                throw new ValidationException("Node key may only contain letters, numbers, underscores, and hyphens.");

            if (string.IsNullOrWhiteSpace(node.Name))
                throw new ValidationException("Node name is required.");

            if (!keys.Add(node.Key))
                throw new ValidationException($"Duplicate workflow node key '{node.Key}'.");
        }

        var edgePairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in command.Edges)
        {
            if (!keys.Contains(edge.From))
                throw new ValidationException($"Workflow edge references unknown from-node '{edge.From}'.");

            if (!keys.Contains(edge.To))
                throw new ValidationException($"Workflow edge references unknown to-node '{edge.To}'.");

            if (edge.From == edge.To)
                throw new ValidationException("Workflow edges cannot point to the same node.");

            if (!edgePairs.Add($"{edge.From}->{edge.To}"))
                throw new ValidationException($"Duplicate workflow edge '{edge.From}->{edge.To}'.");
        }

        if (HasCycle(command.Nodes.Select(n => n.Key), command.Edges))
            throw new ValidationException("Workflow graph must be acyclic.");
    }

    private static bool HasCycle(IEnumerable<string> nodeKeys, IReadOnlyList<WorkflowEdgeInput> edges)
    {
        var outgoing = nodeKeys.ToDictionary(k => k, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in edges)
            outgoing[edge.From].Add(edge.To);

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        return outgoing.Keys.Any(Visit);

        bool Visit(string key)
        {
            if (visited.Contains(key))
                return false;

            if (!visiting.Add(key))
                return true;

            foreach (var next in outgoing[key])
            {
                if (Visit(next))
                    return true;
            }

            visiting.Remove(key);
            visited.Add(key);
            return false;
        }
    }
}
