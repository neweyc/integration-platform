using ControlPlane.Features.Secrets;
using ControlPlane.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Features.AgentTokens;

public static class AgentTokenEndpoints
{
    public static IEndpointRouteBuilder MapAgentTokenEndpoints(this IEndpointRouteBuilder app)
    {
        // User-facing token management — requires JWT auth
        var mgmt = app.MapGroup("/api/agent-tokens").WithTags("AgentTokens").RequireAuthorization();

        mgmt.MapPost("/", async (
            [FromBody] CreateAgentTokenRequest request,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new CreateAgentTokenCommand(currentUser.TenantId, request.Name, request.Environment), ct);

            return Results.Created($"/api/agent-tokens/{result.Id}", result);
        });

        mgmt.MapGet("/", async (
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new ListAgentTokensCommand(currentUser.TenantId), ct);
            return Results.Ok(result);
        });

        mgmt.MapDelete("/{id:guid}", async (
            Guid id,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new RevokeAgentTokenCommand(currentUser.TenantId, id), ct);
            return Results.NoContent();
        });

        // Agent-facing endpoint — authenticated via X-Agent-Token header, not JWT
        app.MapGet("/api/agent/secrets/{environment}", async (
            string environment,
            HttpContext http,
            IAgentTokenService tokenService,
            IAgentTokenLookupRepository tokenRepo,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            // Extract and validate the agent token
            var header = http.Request.Headers["X-Agent-Token"].FirstOrDefault();
            if (string.IsNullOrEmpty(header))
                return Results.Unauthorized();

            var hash = tokenService.Hash(header);
            var agentToken = await tokenRepo.FindByHashAsync(hash, ct);

            if (agentToken is null || agentToken.Environment != environment)
                return Results.Unauthorized();

            var result = await dispatcher.SendAsync(
                new GetSecretBundleCommand(agentToken.TenantId, environment), ct);

            return Results.Ok(result);
        }).WithTags("Agent");

        return app;
    }
}

public record CreateAgentTokenRequest(string Name, string Environment);
