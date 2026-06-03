namespace Shared.Domain;

public class Invitation : Entity
{
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Member;
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
}
