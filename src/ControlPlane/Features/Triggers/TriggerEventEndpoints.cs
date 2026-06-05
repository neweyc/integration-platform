using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Triggers;

public static class TriggerEventEndpoints
{
    public static IEndpointRouteBuilder MapTriggerEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/trigger-events")
            .WithTags("Trigger Events")
            .RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] Guid? integrationId,
            [FromQuery] Guid? triggerId,
            [FromQuery] string? adapterKey,
            [FromQuery] TriggerEventOutcome? outcome,
            [FromQuery] int? limit,
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var take = Math.Clamp(limit ?? 50, 1, 200);
            var query = db.TriggerEvents
                .AsNoTracking()
                .Where(e => e.TenantId == currentUser.TenantId);

            if (integrationId.HasValue)
                query = query.Where(e => e.IntegrationId == integrationId.Value);

            if (triggerId.HasValue)
                query = query.Where(e => e.IntegrationTriggerId == triggerId.Value);

            if (!string.IsNullOrWhiteSpace(adapterKey))
            {
                var normalized = TriggerEventRecorder.NormalizeAdapterKey(adapterKey, TriggerSource.Manual);
                query = query.Where(e => e.AdapterKey == normalized);
            }

            if (outcome.HasValue)
                query = query.Where(e => e.Outcome == outcome.Value);

            var events = await query
                .OrderByDescending(e => e.ReceivedAt)
                .ThenByDescending(e => e.CreatedAt)
                .Take(take)
                .Select(e => new TriggerEventResult(
                    e.Id,
                    e.IntegrationId,
                    e.IntegrationTriggerId,
                    e.AdapterKey,
                    e.Source,
                    e.EventKey,
                    e.Outcome,
                    e.WorkItemId,
                    e.MetadataJson,
                    e.ErrorMessage,
                    e.ReceivedAt))
                .ToListAsync(ct);

            return Results.Ok(new TriggerEventListResult(events));
        }).RequirePermission(Permission.ViewExecutions);

        return app;
    }
}

public record TriggerEventListResult(IReadOnlyList<TriggerEventResult> Events);

public record TriggerEventResult(
    Guid Id,
    Guid IntegrationId,
    Guid? IntegrationTriggerId,
    string AdapterKey,
    TriggerSource Source,
    string? EventKey,
    TriggerEventOutcome Outcome,
    Guid? WorkItemId,
    string? MetadataJson,
    string? ErrorMessage,
    DateTime ReceivedAt);
