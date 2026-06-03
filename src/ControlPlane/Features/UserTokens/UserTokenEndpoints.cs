using ControlPlane.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Features.UserTokens;

public static class UserTokenEndpoints
{
    public static IEndpointRouteBuilder MapUserTokenEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/user-tokens").WithTags("UserTokens").RequireAuthorization();

        group.MapPost("/", async (
            [FromBody] CreateUserTokenRequest request,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new CreateUserTokenCommand(currentUser.TenantId, currentUser.UserId, request.Name), ct);

            return Results.Created($"/api/user-tokens/{result.Id}", result);
        });

        group.MapGet("/", async (
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new ListUserTokensCommand(currentUser.TenantId, currentUser.UserId), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new RevokeUserTokenCommand(currentUser.TenantId, id), ct);
            return Results.NoContent();
        });

        return app;
    }
}

public record CreateUserTokenRequest(string Name);
