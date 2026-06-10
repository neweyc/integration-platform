using ControlPlane.Features.Billing;
using ControlPlane.Infrastructure.Authorization;
using Shared.Domain;

namespace ControlPlane.Features.Licensing;

public static class LicenseEndpoints
{
    public static IEndpointRouteBuilder MapLicenseEndpoints(this IEndpointRouteBuilder app)
    {
        // The current edition/state/expiry plus the caps it entitles. Instance-level (not per-tenant), read
        // straight from the license service. Surfaced so operators can see their entitlement and expiry.
        app.MapGet("/api/license", (ILicenseService license, BillingPlanCatalog planCatalog) =>
            {
                var info = license.Current;

                // The plan that actually governs caps on this self-hosted deployment (base tenant is Free):
                // Free for unlicensed/invalid/expired, the licensed plan while valid or in grace.
                var effectivePlan = license.EffectivePlanFor(BillingPlan.Free);
                var maxIntegrations = planCatalog.MaxIntegrationsFor(effectivePlan);
                var maxEnvironments = planCatalog.MaxEnvironmentsFor(effectivePlan);

                return Results.Ok(new
                {
                    Edition = effectivePlan == BillingPlan.Free ? "Community" : effectivePlan.ToString(),
                    State = info.State.ToString(),
                    info.Licensee,
                    LicensedPlan = info.Plan,
                    info.Expiry,
                    info.GraceUntil,
                    // null = unlimited (paid plans).
                    MaxIntegrations = maxIntegrations == int.MaxValue ? (int?)null : maxIntegrations,
                    MaxEnvironments = maxEnvironments == int.MaxValue ? (int?)null : maxEnvironments
                });
            })
            .RequireAuthorization()
            .RequirePermission(Permission.ManageBilling)
            .WithTags("License");

        return app;
    }
}
