using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (
            [FromBody] RegisterUserRequest request,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new RegisterUserCommand(currentUser.TenantId, request.Email, request.Password), ct);
            return Results.Created($"/api/users/{result.UserId}", new { result.UserId, result.Email });
        }).RequireAuthorization().RequirePermission(Permission.ManageUsers);

        group.MapGet("/users", async (
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new ListUsersCommand(currentUser.TenantId), ct);
            return Results.Ok(result);
        }).RequireAuthorization().RequirePermission(Permission.ManageUsers);

        group.MapPost("/login", async (
            [FromBody] LoginUserRequest request,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new LoginUserCommand(request.Email, request.Password), ct);
            return Results.Ok(result);
        });

        return app;
    }
}

public record RegisterUserRequest(string Email, string Password);
public record LoginUserRequest(string Email, string Password);
