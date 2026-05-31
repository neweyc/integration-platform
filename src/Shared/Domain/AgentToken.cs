namespace Shared.Domain;

public class AgentToken : Entity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    // Human-readable label so users can identify tokens
    public string Name { get; set; } = "";

    // Scopes this token to a single environment
    public string Environment { get; set; } = "";

    // SHA-256 of the plaintext token — plaintext is never stored
    public string TokenHash { get; set; } = "";
}
