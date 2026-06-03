namespace Shared.Domain;

public class UserToken : Entity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
