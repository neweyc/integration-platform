using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Domain;

namespace ControlPlane.Features.Invitations;

public static class InvitationEndpoints
{
    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invitations").WithTags("Invitations").RequireAuthorization();

        group.MapPost("/", async (
            [FromBody] InviteUserRequest request,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new InviteUserCommand(request.Email, request.Role), ct);
            return Results.Ok(result);
        }).RequirePermission(Permission.ManageUsers);

        app.MapPost("/api/invitations/accept", async (
            [FromBody] AcceptInvitationRequest request,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new AcceptInvitationCommand(request.Token, request.Password), ct);
            return Results.Ok(result);
        }).WithTags("Invitations");

        return app;
    }
}

public record InviteUserRequest(string Email, UserRole Role);
public record AcceptInvitationRequest(string Token, string Password);
