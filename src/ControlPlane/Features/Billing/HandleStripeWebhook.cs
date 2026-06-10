using ControlPlane.Infrastructure;
using Shared.Domain;
using Stripe;

namespace ControlPlane.Features.Billing;

// Reconciles tenant billing state from a Stripe subscription webhook. The single source of truth for
// a tenant's plan and quota is Stripe: this handler applies whatever Stripe reports.
public record HandleStripeWebhookCommand(string Payload, string SignatureHeader) : ICommand<bool>;

public class HandleStripeWebhookHandler(
    IBillingRepository repository,
    IStripeGateway gateway,
    BillingPlanCatalog catalog,
    StripeOptions options)
    : ICommandHandler<HandleStripeWebhookCommand, bool>
{
    public async Task<bool> HandleAsync(HandleStripeWebhookCommand command, CancellationToken ct = default)
    {
        // If billing isn't configured, there's nothing to verify against — ignore quietly.
        if (!options.IsConfigured)
            return true;

        StripeSubscriptionEvent evt;
        try
        {
            evt = gateway.ParseEvent(command.Payload, command.SignatureHeader);
        }
        catch (StripeException)
        {
            // A bad/forged signature must not look like a server error.
            throw new ValidationException("Invalid Stripe webhook signature.");
        }

        var tenant = await ResolveTenantAsync(evt, ct);
        if (tenant is null)
            return true; // Unknown tenant/customer — nothing to reconcile.

        if (evt.Type == "customer.subscription.deleted")
        {
            // Subscription ended: fall back to the free plan and its quota.
            tenant.Plan = BillingPlan.Free;
            tenant.SubscriptionStatus = "canceled";
            tenant.StripeSubscriptionId = null;
            tenant.MaxExecutionsPerMonth = catalog.QuotaFor(BillingPlan.Free);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(evt.CustomerId))
                tenant.StripeCustomerId ??= evt.CustomerId;

            if (!string.IsNullOrWhiteSpace(evt.SubscriptionId))
                tenant.StripeSubscriptionId = evt.SubscriptionId;

            if (!string.IsNullOrWhiteSpace(evt.Status))
                tenant.SubscriptionStatus = evt.Status;

            if (!string.IsNullOrWhiteSpace(evt.PriceId))
            {
                var plan = catalog.PlanForPriceId(evt.PriceId);
                tenant.Plan = plan;
                tenant.MaxExecutionsPerMonth = catalog.QuotaFor(plan);
            }
        }

        await repository.UpdateAsync(tenant, ct);
        return true;
    }

    private Task<Tenant?> ResolveTenantAsync(StripeSubscriptionEvent evt, CancellationToken ct)
    {
        if (evt.TenantId is { } tenantId)
            return repository.GetByIdAsync(tenantId, ct);

        if (!string.IsNullOrWhiteSpace(evt.CustomerId))
            return repository.FindByStripeCustomerIdAsync(evt.CustomerId, ct);

        return Task.FromResult<Tenant?>(null);
    }
}
