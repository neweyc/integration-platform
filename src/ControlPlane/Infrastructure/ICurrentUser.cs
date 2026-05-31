namespace ControlPlane.Infrastructure;

public interface ICurrentUser
{
    Guid UserId { get; }
    Guid TenantId { get; }
    string Email { get; }
    bool IsAdmin { get; }
}
