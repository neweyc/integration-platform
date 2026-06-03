using ControlPlane.Features.UserTokens;
using System.Security.Claims;

namespace ControlPlane.Infrastructure;

public class UserTokenAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context, 
        IUserTokenService tokenService, 
        IUserTokenRepository repository)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        
        if (authHeader != null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..].Trim();
            
            if (token.StartsWith("pat_", StringComparison.OrdinalIgnoreCase))
            {
                var hash = tokenService.Hash(token);
                var userToken = await repository.FindByHashAsync(hash, context.RequestAborted);
                
                if (userToken != null)
                {
                    var claims = new List<Claim>
                    {
                        new(ClaimTypes.NameIdentifier, userToken.UserId.ToString()),
                        new("sub", userToken.UserId.ToString()),
                        new(ClaimTypes.Email, userToken.User.Email),
                        new("email", userToken.User.Email),
                        new("tenant_id", userToken.TenantId.ToString()),
                        new("role", userToken.User.Role.ToString())
                    };

                    var identity = new ClaimsIdentity(claims, "UserToken");
                    context.User = new ClaimsPrincipal(identity);
                }
            }
        }

        await next(context);
    }
}

public static class UserTokenAuthenticationExtensions
{
    public static IApplicationBuilder UseUserTokenAuthentication(this IApplicationBuilder app)
    {
        return app.UseMiddleware<UserTokenAuthenticationMiddleware>();
    }
}
