namespace Shared.Domain;

public class ExecutionRecord : Entity
{
    public Guid IntegrationId { get; set; }
    public Integration Integration { get; set; } = null!;

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public string Environment { get; set; } = "";
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Running;
    public TriggerSource TriggerSource { get; set; } = TriggerSource.Scheduled;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum ExecutionStatus
{
    Running,
    Succeeded,
    Failed
}

public enum TriggerSource
{
    Scheduled,
    Manual,
    Webhook
}
