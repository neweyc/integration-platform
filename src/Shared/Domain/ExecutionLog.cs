namespace Shared.Domain;

public class ExecutionLog : Entity
{
    public Guid ExecutionRecordId { get; set; }
    public ExecutionRecord ExecutionRecord { get; set; } = null!;

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public string? PropertiesJson { get; set; }
}
