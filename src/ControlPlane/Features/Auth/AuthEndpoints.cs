using ControlPlane.Infrastructure;
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
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new RegisterUserCommand(request.TenantId, request.Email, request.Password), ct);
            return Results.Created($"/api/users/{result.UserId}", new { result.UserId, result.Email });
        });

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

public record RegisterUserRequest(Guid TenantId, string Email, string Password);
public record LoginUserRequest(string Email, string Password);
