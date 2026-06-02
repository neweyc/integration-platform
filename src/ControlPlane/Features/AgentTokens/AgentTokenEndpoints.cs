using ControlPlane.Features.IntegrationPackages;
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

        // All agent-facing endpoints share the same token validation pattern
        var agent = app.MapGroup("/api/agent").WithTags("Agent");

        agent.MapGet("/secrets/{environment}", async (
            string environment,
            HttpContext http,
            IAgentTokenService tokenService,
            IAgentTokenLookupRepository tokenRepo,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var agentToken = await ResolveAgentToken(http, environment, tokenService, tokenRepo, ct);
            if (agentToken is null) return Results.Unauthorized();

            var result = await dispatcher.SendAsync(
                new GetSecretBundleCommand(agentToken.TenantId, environment), ct);

            return Results.Ok(result);
        });

        agent.MapGet("/integrations", async (
            HttpContext http,
            IAgentTokenService tokenService,
            IAgentTokenLookupRepository tokenRepo,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var header = http.Request.Headers["X-Agent-Token"].FirstOrDefault();
            if (string.IsNullOrEmpty(header)) return Results.Unauthorized();

            var hash = tokenService.Hash(header);
            var agentToken = await tokenRepo.FindByHashAsync(hash, ct);
            if (agentToken is null) return Results.Unauthorized();

            // Use the agent token ID as the lease owner to track which agent claimed work
            var result = await dispatcher.SendAsync(
                new PollIntegrationsCommand(agentToken.TenantId, agentToken.Environment, agentToken.Id), ct);

            return Results.Ok(result);
        });

        agent.MapGet("/packages", async (
            HttpContext http,
            IAgentTokenService tokenService,
            IAgentTokenLookupRepository tokenRepo,
            IPackageReadRepository packageRepo,
            CancellationToken ct) =>
        {
            var header = http.Request.Headers["X-Agent-Token"].FirstOrDefault();
            if (string.IsNullOrEmpty(header)) return Results.Unauthorized();

            var hash = tokenService.Hash(header);
            var agentToken = await tokenRepo.FindByHashAsync(hash, ct);
            if (agentToken is null) return Results.Unauthorized();

            var packages = await packageRepo.ListAsync(agentToken.TenantId, ct);
            var items = packages
                .Select(p => new AgentPackageItem(p.Id, p.Name, p.Version, p.Sha256Hash))
                .ToList();

            return Results.Ok(new AgentPackagesResponse(items));
        });

        agent.MapGet("/packages/{id:guid}/download", async (
            Guid id,
            HttpContext http,
            IAgentTokenService tokenService,
            IAgentTokenLookupRepository tokenRepo,
            IPackageReadRepository packageRepo,
            CancellationToken ct) =>
        {
            var header = http.Request.Headers["X-Agent-Token"].FirstOrDefault();
            if (string.IsNullOrEmpty(header)) return Results.Unauthorized();

            var hash = tokenService.Hash(header);
            var agentToken = await tokenRepo.FindByHashAsync(hash, ct);
            if (agentToken is null) return Results.Unauthorized();

            var package = await packageRepo.GetAsync(agentToken.TenantId, id, ct);
            if (package is null) return Results.NotFound();

            return Results.File(package.Data, "application/zip", package.FileName);
        });

        agent.MapPost("/executions", async (
            [FromBody] StartExecutionRequest request,
            HttpContext http,
            IAgentTokenService tokenService,
            IAgentTokenLookupRepository tokenRepo,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var header = http.Request.Headers["X-Agent-Token"].FirstOrDefault();
            if (string.IsNullOrEmpty(header)) return Results.Unauthorized();

            var hash = tokenService.Hash(header);
            var agentToken = await tokenRepo.FindByHashAsync(hash, ct);
            if (agentToken is null) return Results.Unauthorized();

            var result = await dispatcher.SendAsync(
                new StartExecutionCommand(
                    agentToken.TenantId,
                    agentToken.Environment,
                    request.IntegrationId,
                    agentToken.Id,
                    request.TriggerSource,
                    request.ManualRunRequestId), ct);

            return Results.Created($"/api/agent/executions/{result.ExecutionId}", result);
        });

        agent.MapPut("/executions/{id:guid}", async (
            Guid id,
            [FromBody] CompleteExecutionRequest request,
            HttpContext http,
            IAgentTokenService tokenService,
            IAgentTokenLookupRepository tokenRepo,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var header = http.Request.Headers["X-Agent-Token"].FirstOrDefault();
            if (string.IsNullOrEmpty(header)) return Results.Unauthorized();

            var hash = tokenService.Hash(header);
            var agentToken = await tokenRepo.FindByHashAsync(hash, ct);
            if (agentToken is null) return Results.Unauthorized();

            await dispatcher.SendAsync(
                new CompleteExecutionCommand(agentToken.TenantId, id, request.Succeeded, request.ErrorMessage, request.IsTimeout), ct);

            return Results.NoContent();
        });

        agent.MapPost("/executions/{id:guid}/logs", async (
            Guid id,
            [FromBody] RecordExecutionLogRequest request,
            HttpContext http,
            IAgentTokenService tokenService,
            IAgentTokenLookupRepository tokenRepo,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var header = http.Request.Headers["X-Agent-Token"].FirstOrDefault();
            if (string.IsNullOrEmpty(header)) return Results.Unauthorized();

            var hash = tokenService.Hash(header);
            var agentToken = await tokenRepo.FindByHashAsync(hash, ct);
            if (agentToken is null) return Results.Unauthorized();

            await dispatcher.SendAsync(
                new RecordExecutionLogCommand(
                    agentToken.TenantId,
                    agentToken.Environment,
                    id,
                    request.Timestamp,
                    request.Level,
                    request.Message,
                    request.Exception,
                    request.PropertiesJson),
                ct);

            return Results.NoContent();
        });

        return app;
    }

    // Validates X-Agent-Token and checks environment scope
    private static async Task<Shared.Domain.AgentToken?> ResolveAgentToken(
        HttpContext http,
        string environment,
        IAgentTokenService tokenService,
        IAgentTokenLookupRepository tokenRepo,
        CancellationToken ct)
    {
        var header = http.Request.Headers["X-Agent-Token"].FirstOrDefault();
        if (string.IsNullOrEmpty(header)) return null;

        var hash = tokenService.Hash(header);
        var agentToken = await tokenRepo.FindByHashAsync(hash, ct);

        return agentToken?.Environment == environment ? agentToken : null;
    }
}

public record AgentPackageItem(Guid Id, string Name, string Version, string Sha256Hash);
public record AgentPackagesResponse(IReadOnlyList<AgentPackageItem> Packages);

public record CreateAgentTokenRequest(string Name, string Environment);
public record StartExecutionRequest(Guid IntegrationId, string TriggerSource = "Scheduled", Guid? ManualRunRequestId = null);
public record CompleteExecutionRequest(bool Succeeded, string? ErrorMessage, bool IsTimeout = false);
public record RecordExecutionLogRequest(
    DateTime Timestamp,
    string Level,
    string Message,
    string? Exception,
    string? PropertiesJson);
