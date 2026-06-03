using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Features.Secrets;

public static class SecretEndpoints
{
    public static IEndpointRouteBuilder MapSecretEndpoints(this IEndpointRouteBuilder app)
    {
        // All secret endpoints require authentication and are scoped to the current user's tenant
        var group = app.MapGroup("/api/secrets").WithTags("Secrets").RequireAuthorization();

        // Create or update a secret value
        group.MapPut("/{environment}/{key}", async (
            string environment,
            string key,
            [FromBody] SetSecretRequest request,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new SetSecretCommand(currentUser.TenantId, environment, key, request.Value), ct);

            return Results.Ok(result);
        }).RequirePermission(Permission.ManageSecrets);

        // List all secret keys for an environment (values are never returned)
        group.MapGet("/{environment}", async (
            string environment,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new ListSecretsCommand(currentUser.TenantId, environment), ct);

            return Results.Ok(result);
        }).RequirePermission(Permission.ViewSecrets);

        // Delete a secret
        group.MapDelete("/{environment}/{key}", async (
            string environment,
            string key,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            await dispatcher.SendAsync(
                new DeleteSecretCommand(currentUser.TenantId, environment, key), ct);

            return Results.NoContent();
        }).RequirePermission(Permission.ManageSecrets);

        return app;
    }
}

public record SetSecretRequest(string Value);
