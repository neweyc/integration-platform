using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Authorization;
using ControlPlane.Infrastructure.RateLimiting;
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
        }).RequireRateLimiting(RateLimitOptions.AuthPolicy);

        // Exchange a refresh token for a new access + refresh token pair (token rotation).
        group.MapPost("/refresh", async (
            [FromBody] RefreshTokenRequest request,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new RefreshSessionCommand(request.RefreshToken), ct);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitOptions.AuthPolicy);

        // Revoke a refresh token so it can no longer be exchanged. Idempotent and unauthenticated:
        // presenting the token to be revoked is the only credential needed.
        group.MapPost("/logout", async (
            [FromBody] RefreshTokenRequest request,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new LogoutCommand(request.RefreshToken), ct);
            return Results.NoContent();
        }).RequireRateLimiting(RateLimitOptions.AuthPolicy);

        // Begin a password reset. Always returns 204 so it can't be used to discover which emails
        // have accounts; if the email matches a user, a reset link is sent.
        group.MapPost("/forgot-password", async (
            [FromBody] ForgotPasswordRequest request,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new ForgotPasswordCommand(request.Email), ct);
            return Results.NoContent();
        }).RequireRateLimiting(RateLimitOptions.AuthPolicy);

        // Complete a password reset using the emailed token.
        group.MapPost("/reset-password", async (
            [FromBody] ResetPasswordRequest request,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new ResetPasswordCommand(request.Token, request.NewPassword), ct);
            return Results.NoContent();
        }).RequireRateLimiting(RateLimitOptions.AuthPolicy);

        return app;
    }
}

public record RegisterUserRequest(string Email, string Password);
public record LoginUserRequest(string Email, string Password);
public record RefreshTokenRequest(string RefreshToken);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword);
