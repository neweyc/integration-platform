namespace ControlPlane.Infrastructure.Authorization;

public static class RequirePermissionExtensions
{
    /// <summary>
    /// Adds server-side permission enforcement to an endpoint or group. Returns 401 if the
    /// caller is unauthenticated and 403 if their role does not grant the required permission.
    /// Apply after <c>RequireAuthorization()</c>.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, Permission permission)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var user = ctx.HttpContext.User;
            if (user.Identity?.IsAuthenticated != true)
                throw new UnauthorizedException("Authentication required.");

            var currentUser = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

            if (!RolePermissions.IsGranted(currentUser.Role, permission))
                throw new ForbiddenException(
                    $"Role '{currentUser.Role}' does not have permission '{permission}'.");

            return await next(ctx);
        });

        return builder;
    }
}
