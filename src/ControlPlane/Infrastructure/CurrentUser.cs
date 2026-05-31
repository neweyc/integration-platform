using System.Security.Claims;

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

    public bool IsAdmin => User.FindFirstValue("role") == "Admin";
}
