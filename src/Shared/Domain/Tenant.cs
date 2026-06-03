namespace Shared.Domain;

public class Tenant : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public int MaxExecutionsPerMonth { get; set; } = 1000; // Default limit for new tenants
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
}

public enum TenantStatus
{
    Active,
    Suspended,
    Deleted
}
