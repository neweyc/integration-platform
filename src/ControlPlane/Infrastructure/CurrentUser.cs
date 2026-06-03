using System.Security.Claims;
using Shared.Domain;

namespace ControlPlane.Infrastructure;

public class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal User => httpContextAccessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HTTP context available.");

    public Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? throw new InvalidOperationException("User ID claim missing."));

    public Guid TenantId => Guid.Parse(User.FindFirstValue("tenant_id")
        ?? throw new InvalidOperationException("Tenant ID claim missing."));

    public string Email => User.FindFirstValue(ClaimTypes.Email)
        ?? User.FindFirstValue("email")
        ?? throw new InvalidOperationException("Email claim missing.");

    public UserRole Role
    {
        get
        {
            // JWT validation may remap the short "role" claim to ClaimTypes.Role; the
            // user-token middleware emits the short form. Accept either.
            var raw = User.FindFirstValue("role") ?? User.FindFirstValue(ClaimTypes.Role);
            return Enum.TryParse<UserRole>(raw, out var role)
                ? role
                // Unknown/missing role claim defaults to the least-privileged role.
                : UserRole.Member;
        }
    }

    public bool IsAdmin => Role == UserRole.Admin;
}
