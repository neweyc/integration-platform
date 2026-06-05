using ControlPlane.Infrastructure.Authorization;

namespace ControlPlane.Features.Triggers;

public static class TriggerAdapterEndpoints
{
    public static IEndpointRouteBuilder MapTriggerAdapterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/trigger-adapters")
            .WithTags("Trigger Adapters")
            .RequireAuthorization();

        group.MapGet("/", (ITriggerAdapterCatalog catalog) =>
            Results.Ok(new TriggerAdapterListResult(catalog.List())))
            .RequirePermission(Permission.ViewIntegrations);

        return app;
    }
}

public record TriggerAdapterListResult(IReadOnlyList<TriggerAdapterDescriptor> Adapters);
