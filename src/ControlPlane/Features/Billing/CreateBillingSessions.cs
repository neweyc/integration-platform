using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Billing;

// Starts a Stripe Checkout for a self-serve plan and returns the hosted-checkout URL to redirect to.
public record CreateCheckoutSessionCommand(Guid TenantId, string Plan) : ICommand<BillingUrlResult>;

// Opens the Stripe Billing Portal so an admin can manage their existing subscription/payment method.
public record CreatePortalSessionCommand(Guid TenantId) : ICommand<BillingUrlResult>;

public record BillingUrlResult(string Url);

public class CreateCheckoutSessionHandler(
    IBillingRepository repository,
    IStripeGateway gateway,
    BillingPlanCatalog catalog,
    StripeOptions options,
    IConfiguration configuration)
    : ICommandHandler<CreateCheckoutSessionCommand, BillingUrlResult>
{
    public async Task<BillingUrlResult> HandleAsync(CreateCheckoutSessionCommand command, CancellationToken ct = default)
    {
        if (!options.IsConfigured)
            throw new ValidationException("Billing is not configured on this server.");

        if (!Enum.TryParse<BillingPlan>(command.Plan, ignoreCase: true, out var plan))
            throw new ValidationException($"Unknown plan '{command.Plan}'.");

        var priceId = catalog.PriceIdFor(plan);
        if (string.IsNullOrWhiteSpace(priceId))
            throw new ValidationException($"The {plan} plan is not available for self-serve checkout.");

        var baseUrl = BillingUrls.RequireBaseUrl(configuration);
        var tenant = await repository.GetByIdAsync(command.TenantId, ct)
            ?? throw new NotFoundException("Tenant not found.");

        var url = await gateway.CreateCheckoutSessionAsync(new CheckoutRequest(
            command.TenantId,
            priceId,
            tenant.StripeCustomerId,
            SuccessUrl: $"{baseUrl}/billing?checkout=success",
            CancelUrl: $"{baseUrl}/billing?checkout=cancel"), ct);

        return new BillingUrlResult(url);
    }
}

public class CreatePortalSessionHandler(
    IBillingRepository repository,
    IStripeGateway gateway,
    StripeOptions options,
    IConfiguration configuration)
    : ICommandHandler<CreatePortalSessionCommand, BillingUrlResult>
{
    public async Task<BillingUrlResult> HandleAsync(CreatePortalSessionCommand command, CancellationToken ct = default)
    {
        if (!options.IsConfigured)
            throw new ValidationException("Billing is not configured on this server.");

        var tenant = await repository.GetByIdAsync(command.TenantId, ct)
            ?? throw new NotFoundException("Tenant not found.");

        if (string.IsNullOrWhiteSpace(tenant.StripeCustomerId))
            throw new ValidationException("No billing account exists yet. Start a subscription first.");

        var baseUrl = BillingUrls.RequireBaseUrl(configuration);
        var url = await gateway.CreatePortalSessionAsync(tenant.StripeCustomerId, $"{baseUrl}/billing", ct);

        return new BillingUrlResult(url);
    }
}

internal static class BillingUrls
{
    public static string RequireBaseUrl(IConfiguration configuration)
    {
        var baseUrl = configuration["App:BaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ValidationException("App:BaseUrl must be configured to use billing checkout/portal redirects.");
        return baseUrl;
    }
}
