namespace ControlPlane.Features.Billing;

public record CheckoutRequest(
    Guid TenantId,
    string PriceId,
    string? CustomerId,
    string SuccessUrl,
    string CancelUrl);

// A normalized view of the Stripe webhook events we act on, so handlers never touch Stripe SDK types.
public record StripeSubscriptionEvent(
    string Type,
    string? CustomerId,
    string? SubscriptionId,
    string? Status,
    string? PriceId,
    Guid? TenantId);

// Wraps every call into the Stripe SDK so the rest of the feature stays SDK-free and unit-testable.
public interface IStripeGateway
{
    Task<string> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken ct = default);
    Task<string> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken ct = default);

    // Verifies the webhook signature and normalizes the event; throws if the signature is invalid.
    StripeSubscriptionEvent ParseEvent(string payload, string signatureHeader);
}
