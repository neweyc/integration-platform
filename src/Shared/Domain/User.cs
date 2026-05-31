namespace Shared.Domain;

public class User : Entity
{
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Member;

    public Tenant Tenant { get; set; } = null!;
}

public enum UserRole
{
    Admin,
    Member
}
