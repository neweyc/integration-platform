using ControlPlane.Features.Licensing;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Licensing;

// A license service that leaves the tenant's plan untouched — the unlicensed Community baseline. Used by
// handler tests that aren't exercising license behavior so they read like before the license seam existed.
public sealed class PassThroughLicenseService : ILicenseService
{
    public LicenseInfo Current => new(LicenseState.Unlicensed, null, BillingPlan.Free, null, null);
    public BillingPlan EffectivePlanFor(BillingPlan tenantPlan) => tenantPlan;
}

// A license service entitling a fixed plan, as a valid commercial license would. Used to prove a license
// lifts the plan caps.
public sealed class FixedPlanLicenseService(BillingPlan plan) : ILicenseService
{
    public LicenseInfo Current =>
        new(LicenseState.Valid, "Test Licensee", plan, DateTime.UtcNow.AddYears(1), DateTime.UtcNow.AddYears(1));

    public BillingPlan EffectivePlanFor(BillingPlan tenantPlan) =>
        (BillingPlan)Math.Max((int)plan, (int)tenantPlan);
}
