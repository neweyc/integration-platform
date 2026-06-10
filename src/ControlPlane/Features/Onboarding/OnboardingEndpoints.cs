using ControlPlane.Infrastructure;

namespace ControlPlane.Features.Onboarding;

public static class OnboardingEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/onboarding")
            .WithTags("Onboarding")
            .RequireAuthorization();

        // Reports first-run progress for the current tenant so the UI can show a getting-started
        // checklist. Available to any authenticated user — it's informational, not privileged.
        group.MapGet("/status", async (
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new GetOnboardingStatusCommand(currentUser.TenantId), ct);
            return Results.Ok(result);
        });

        return app;
    }
}
