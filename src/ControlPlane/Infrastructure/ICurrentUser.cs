using Shared.Domain;

namespace ControlPlane.Infrastructure;

public interface ICurrentUser
{
    Guid UserId { get; }
    Guid TenantId { get; }
    string Email { get; }
    UserRole Role { get; }
    bool IsAdmin { get; }
}
