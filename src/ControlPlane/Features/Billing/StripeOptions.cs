using Shared.Domain;

namespace ControlPlane.Features.Billing;

// Stripe configuration from the "Stripe" config section. Billing is entirely optional: with no
// SecretKey the whole feature is inert (status still reports the tenant's stored plan, but checkout,
// portal, and webhook calls are rejected/ignored). Operators who don't sell hosted plans leave it blank.
public class StripeOptions
{
    public string? SecretKey { get; set; }
    public string? WebhookSecret { get; set; }

    // Stripe Price ids for the self-serve plans. Enterprise is sales-assisted, so it has no price here.
    public string? TeamPriceId { get; set; }
    public string? BusinessPriceId { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);
}

// Maps billing plans to their Stripe price and monthly execution quota, in both directions: plan →
// price for checkout, and price → plan for reconciling subscription webhooks.
public class BillingPlanCatalog(StripeOptions options)
{
    public int QuotaFor(BillingPlan plan) => plan switch
    {
        BillingPlan.Free => 1_000,
        BillingPlan.Team => 10_000,
        BillingPlan.Business => 100_000,
        BillingPlan.Enterprise => 1_000_000,
        _ => 1_000
    };

    // Maximum environments a plan may have. Free is capped (one extra beyond the seeded production
    // environment); paid plans are effectively unlimited.
    public int MaxEnvironmentsFor(BillingPlan plan) => plan switch
    {
        BillingPlan.Free => 2,
        _ => int.MaxValue
    };

    // The Stripe price id to check out for a plan, or null for plans that aren't self-serve
    // (Free has no charge; Enterprise is sales-assisted).
    public string? PriceIdFor(BillingPlan plan) => plan switch
    {
        BillingPlan.Team => options.TeamPriceId,
        BillingPlan.Business => options.BusinessPriceId,
        _ => null
    };

    // Resolves the plan a Stripe price id corresponds to. Unknown prices fall back to Free so an
    // unexpected subscription never silently grants a higher quota.
    public BillingPlan PlanForPriceId(string? priceId)
    {
        if (!string.IsNullOrWhiteSpace(priceId) && priceId == options.TeamPriceId) return BillingPlan.Team;
        if (!string.IsNullOrWhiteSpace(priceId) && priceId == options.BusinessPriceId) return BillingPlan.Business;
        return BillingPlan.Free;
    }
}
