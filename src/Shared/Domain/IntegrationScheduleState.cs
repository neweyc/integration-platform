namespace Shared.Domain;

public class IntegrationScheduleState : Entity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public Guid IntegrationId { get; set; }
    public Integration Integration { get; set; } = null!;

    public Guid IntegrationTriggerId { get; set; }
    public IntegrationTrigger IntegrationTrigger { get; set; } = null!;

    public DateTime? LastDispatchedAt { get; set; }
    public DateTime? NextRunAt { get; set; }
}
