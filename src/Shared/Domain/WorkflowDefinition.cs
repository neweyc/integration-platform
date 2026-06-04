namespace Shared.Domain;

public class WorkflowDefinition : Entity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public WorkflowStatus Status { get; set; } = WorkflowStatus.Enabled;

    public ICollection<WorkflowNode> Nodes { get; set; } = [];
    public ICollection<WorkflowEdge> Edges { get; set; } = [];
}

public class WorkflowNode : Entity
{
    public Guid TenantId { get; set; }
    public Guid WorkflowDefinitionId { get; set; }
    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;

    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid IntegrationId { get; set; }
    public Integration Integration { get; set; } = null!;
}

public class WorkflowEdge : Entity
{
    public Guid TenantId { get; set; }
    public Guid WorkflowDefinitionId { get; set; }
    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;

    public Guid FromNodeId { get; set; }
    public WorkflowNode FromNode { get; set; } = null!;

    public Guid ToNodeId { get; set; }
    public WorkflowNode ToNode { get; set; } = null!;
}

public enum WorkflowStatus
{
    Enabled,
    Disabled
}
