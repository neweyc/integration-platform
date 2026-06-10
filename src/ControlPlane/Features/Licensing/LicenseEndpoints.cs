using ControlPlane.Infrastructure.Authorization;

namespace ControlPlane.Features.Licensing;

public static class LicenseEndpoints
{
    public static IEndpointRouteBuilder MapLicenseEndpoints(this IEndpointRouteBuilder app)
    {
        // The current edition/state/expiry. Instance-level (not per-tenant), read straight from the
        // license service. Surfaced so operators can see their entitlement and expiry. Full UI is step 3.
        app.MapGet("/api/license", (ILicenseService license) =>
            {
                var info = license.Current;
                return Results.Ok(new
                {
                    Edition = info.State == LicenseState.Unlicensed || info.State == LicenseState.Invalid
                        ? "Community"
                        : info.Plan.ToString(),
                    State = info.State.ToString(),
                    info.Licensee,
                    info.Plan,
                    info.Expiry,
                    info.GraceUntil
                });
            })
            .RequireAuthorization()
            .RequirePermission(Permission.ManageBilling)
            .WithTags("License");

        return app;
    }
}
