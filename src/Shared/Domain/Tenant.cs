namespace Shared.Domain;

public class Tenant : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public int MaxExecutionsPerMonth { get; set; } = 1000; // Default limit for new tenants

    // Commercial / billing state. Plan drives the execution quota; the Stripe ids and status are
    // synced from Stripe subscription webhooks. A tenant with no paid subscription stays on Free.
    public BillingPlan Plan { get; set; } = BillingPlan.Free;
    public string? SubscriptionStatus { get; set; } // Stripe status, e.g. "active", "past_due", "canceled".
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
}

public enum TenantStatus
{
    Active,
    Suspended,
    Deleted
}

public enum BillingPlan
{
    Free,
    Team,
    Business,
    Enterprise
}
