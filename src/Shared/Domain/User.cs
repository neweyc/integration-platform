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
    // Full control: everything below, plus user management and billing.
    Admin,

    // Deploy and operate integrations; manage secrets, packages, agent tokens. No user mgmt or billing.
    Developer,

    // View executions/logs and trigger manual runs. No secrets, no deploy.
    Operator,

    // Legacy read-only role. Retained for backward compatibility with existing data.
    Member
}
