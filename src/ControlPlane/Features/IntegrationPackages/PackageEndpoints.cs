using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Features.IntegrationPackages;

public static class PackageEndpoints
{
    public static IEndpointRouteBuilder MapPackageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/integration-packages")
            .WithTags("IntegrationPackages")
            .RequireAuthorization();

        group.MapPost("/", async (
            [FromForm] UploadPackageRequest request,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            await using var stream = request.File.OpenReadStream();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, ct);

            var result = await dispatcher.SendAsync(
                new UploadPackageCommand(
                    currentUser.TenantId,
                    request.Name,
                    request.Version,
                    request.File.FileName,
                    memory.ToArray()),
                ct);

            return Results.Created($"/api/integration-packages/{result.Id}", result);
        }).DisableAntiforgery().RequirePermission(Permission.ManagePackages);

        group.MapGet("/", async (
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new ListPackagesCommand(currentUser.TenantId), ct);
            return Results.Ok(result);
        }).RequirePermission(Permission.ViewIntegrations);

        group.MapGet("/{id:guid}", async (
            Guid id,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new GetPackageCommand(currentUser.TenantId, id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission(Permission.ViewIntegrations);

        group.MapGet("/{id:guid}/download", async (
            Guid id,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new DownloadPackageCommand(currentUser.TenantId, id), ct);

            return result is null
                ? Results.NotFound()
                : Results.File(result.Data, result.ContentType, result.FileName);
        }).RequirePermission(Permission.ManagePackages);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new DeletePackageCommand(currentUser.TenantId, id), ct);
            return Results.NoContent();
        }).RequirePermission(Permission.ManagePackages);

        return app;
    }
}

public record UploadPackageRequest(
    string Name,
    string Version,
    IFormFile File);
