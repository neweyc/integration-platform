using Stripe;

namespace ControlPlane.Features.Billing;

// The only type that talks to the Stripe SDK. Everything else in the feature works against
// IStripeGateway and the normalized records, so handlers can be tested with a fake.
public class StripeGateway(StripeOptions options) : IStripeGateway
{
    public async Task<string> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        var service = new Stripe.Checkout.SessionService(CreateClient());
        var session = await service.CreateAsync(new Stripe.Checkout.SessionCreateOptions
        {
            Mode = "subscription",
            LineItems = [new Stripe.Checkout.SessionLineItemOptions { Price = request.PriceId, Quantity = 1 }],
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            Customer = request.CustomerId,
            // Both identifiers let the webhook tie the resulting subscription back to the tenant.
            ClientReferenceId = request.TenantId.ToString(),
            SubscriptionData = new Stripe.Checkout.SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string> { ["tenant_id"] = request.TenantId.ToString() }
            }
        }, cancellationToken: ct);

        return session.Url;
    }

    public async Task<string> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken ct = default)
    {
        var service = new Stripe.BillingPortal.SessionService(CreateClient());
        var session = await service.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = customerId,
            ReturnUrl = returnUrl
        }, cancellationToken: ct);

        return session.Url;
    }

    public StripeSubscriptionEvent ParseEvent(string payload, string signatureHeader)
    {
        var stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, options.WebhookSecret);

        switch (stripeEvent.Type)
        {
            case "customer.subscription.created":
            case "customer.subscription.updated":
            case "customer.subscription.deleted":
                var subscription = (Subscription)stripeEvent.Data.Object;
                return new StripeSubscriptionEvent(
                    stripeEvent.Type,
                    subscription.CustomerId,
                    subscription.Id,
                    subscription.Status,
                    subscription.Items?.Data?.FirstOrDefault()?.Price?.Id,
                    TenantIdFromMetadata(subscription.Metadata));

            case "checkout.session.completed":
                var session = (Stripe.Checkout.Session)stripeEvent.Data.Object;
                return new StripeSubscriptionEvent(
                    stripeEvent.Type,
                    session.CustomerId,
                    session.SubscriptionId,
                    null,
                    null,
                    TenantIdFromString(session.ClientReferenceId));

            default:
                return new StripeSubscriptionEvent(stripeEvent.Type, null, null, null, null, null);
        }
    }

    private static Guid? TenantIdFromMetadata(IDictionary<string, string>? metadata) =>
        metadata is not null && metadata.TryGetValue("tenant_id", out var value) && Guid.TryParse(value, out var id)
            ? id
            : null;

    private static Guid? TenantIdFromString(string? value) =>
        Guid.TryParse(value, out var id) ? id : null;

    private StripeClient CreateClient() => new(options.SecretKey);
}
