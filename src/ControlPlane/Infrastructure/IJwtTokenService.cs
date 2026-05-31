using Shared.Domain;

namespace ControlPlane.Infrastructure;

public interface IJwtTokenService
{
    string GenerateToken(User user);
}
