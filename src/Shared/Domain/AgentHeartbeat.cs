namespace Shared.Domain;

public class AgentHeartbeat : Entity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public Guid AgentTokenId { get; set; }
    public AgentToken AgentToken { get; set; } = null!;

    public string Environment { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Hostname { get; set; }
    public int CurrentConcurrency { get; set; }
    public int MaxConcurrency { get; set; }
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
