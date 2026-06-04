namespace Shared.Domain;

public class WorkflowRun : Entity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public Guid WorkflowDefinitionId { get; set; }
    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;

    public WorkflowRunStatus Status { get; set; } = WorkflowRunStatus.Running;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public ICollection<WorkflowNodeRun> NodeRuns { get; set; } = [];
}

public class WorkflowNodeRun : Entity
{
    public Guid TenantId { get; set; }

    public Guid WorkflowRunId { get; set; }
    public WorkflowRun WorkflowRun { get; set; } = null!;

    public Guid WorkflowNodeId { get; set; }
    public WorkflowNode WorkflowNode { get; set; } = null!;

    public WorkflowNodeRunStatus Status { get; set; } = WorkflowNodeRunStatus.Pending;
    public Guid? WorkItemId { get; set; }
    public WorkItem? WorkItem { get; set; }
    public Guid? ExecutionRecordId { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public enum WorkflowRunStatus
{
    Running,
    Succeeded,
    Failed
}

public enum WorkflowNodeRunStatus
{
    Pending,
    Queued,
    Running,
    Succeeded,
    Failed
}
