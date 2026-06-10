using ControlPlane.Features.Billing;
using ControlPlane.Infrastructure;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Billing;

public class BillingHandlerTests
{
    private static readonly StripeOptions ConfiguredOptions = new()
    {
        SecretKey = "sk_test_123",
        WebhookSecret = "whsec_123",
        TeamPriceId = "price_team",
        BusinessPriceId = "price_business"
    };

    private static BillingPlanCatalog Catalog => new(ConfiguredOptions);

    private static IConfiguration Config(string? baseUrl = "https://serto.test") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = baseUrl })
            .Build();

    // --- Webhook reconciliation ---

    [Fact]
    public async Task Webhook_SubscriptionUpdated_AppliesPlanQuotaAndStatus()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new Tenant { Id = tenantId, Plan = BillingPlan.Free, MaxExecutionsPerMonth = 1000 };

        var repository = Substitute.For<IBillingRepository>();
        repository.GetByIdAsync(tenantId).Returns(tenant);

        var gateway = Substitute.For<IStripeGateway>();
        gateway.ParseEvent(Arg.Any<string>(), Arg.Any<string>()).Returns(
            new StripeSubscriptionEvent("customer.subscription.updated", "cus_1", "sub_1", "active", "price_team", tenantId));

        var handler = new HandleStripeWebhookHandler(repository, gateway, Catalog, ConfiguredOptions);
        await handler.HandleAsync(new HandleStripeWebhookCommand("{}", "sig"));

        Assert.Equal(BillingPlan.Team, tenant.Plan);
        Assert.Equal(10_000, tenant.MaxExecutionsPerMonth);
        Assert.Equal("active", tenant.SubscriptionStatus);
        Assert.Equal("sub_1", tenant.StripeSubscriptionId);
        await repository.Received(1).UpdateAsync(tenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Webhook_SubscriptionDeleted_DowngradesToFree()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new Tenant
        {
            Id = tenantId,
            Plan = BillingPlan.Team,
            MaxExecutionsPerMonth = 10_000,
            StripeSubscriptionId = "sub_1"
        };

        var repository = Substitute.For<IBillingRepository>();
        repository.GetByIdAsync(tenantId).Returns(tenant);

        var gateway = Substitute.For<IStripeGateway>();
        gateway.ParseEvent(Arg.Any<string>(), Arg.Any<string>()).Returns(
            new StripeSubscriptionEvent("customer.subscription.deleted", "cus_1", "sub_1", "canceled", null, tenantId));

        var handler = new HandleStripeWebhookHandler(repository, gateway, Catalog, ConfiguredOptions);
        await handler.HandleAsync(new HandleStripeWebhookCommand("{}", "sig"));

        Assert.Equal(BillingPlan.Free, tenant.Plan);
        Assert.Equal(1_000, tenant.MaxExecutionsPerMonth);
        Assert.Equal("canceled", tenant.SubscriptionStatus);
        Assert.Null(tenant.StripeSubscriptionId);
    }

    [Fact]
    public async Task Webhook_UnknownTenant_DoesNothing()
    {
        var repository = Substitute.For<IBillingRepository>();
        repository.FindByStripeCustomerIdAsync(Arg.Any<string>()).Returns((Tenant?)null);

        var gateway = Substitute.For<IStripeGateway>();
        gateway.ParseEvent(Arg.Any<string>(), Arg.Any<string>()).Returns(
            new StripeSubscriptionEvent("customer.subscription.updated", "cus_unknown", "sub_1", "active", "price_team", null));

        var handler = new HandleStripeWebhookHandler(repository, gateway, Catalog, ConfiguredOptions);
        var result = await handler.HandleAsync(new HandleStripeWebhookCommand("{}", "sig"));

        Assert.True(result);
        await repository.DidNotReceive().UpdateAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Webhook_WhenBillingNotConfigured_IsIgnored()
    {
        var gateway = Substitute.For<IStripeGateway>();
        var handler = new HandleStripeWebhookHandler(
            Substitute.For<IBillingRepository>(), gateway, new BillingPlanCatalog(new StripeOptions()), new StripeOptions());

        var result = await handler.HandleAsync(new HandleStripeWebhookCommand("{}", "sig"));

        Assert.True(result);
        gateway.DidNotReceive().ParseEvent(Arg.Any<string>(), Arg.Any<string>());
    }

    // --- Checkout ---

    [Fact]
    public async Task Checkout_ForTeamPlan_ReturnsStripeUrl()
    {
        var tenantId = Guid.NewGuid();
        var repository = Substitute.For<IBillingRepository>();
        repository.GetByIdAsync(tenantId).Returns(new Tenant { Id = tenantId });

        var gateway = Substitute.For<IStripeGateway>();
        gateway.CreateCheckoutSessionAsync(Arg.Any<CheckoutRequest>(), Arg.Any<CancellationToken>())
            .Returns("https://checkout.stripe.test/session");

        var handler = new CreateCheckoutSessionHandler(repository, gateway, Catalog, ConfiguredOptions, Config());
        var result = await handler.HandleAsync(new CreateCheckoutSessionCommand(tenantId, "Team"));

        Assert.Equal("https://checkout.stripe.test/session", result.Url);
        await gateway.Received(1).CreateCheckoutSessionAsync(
            Arg.Is<CheckoutRequest>(r => r.PriceId == "price_team" && r.TenantId == tenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Checkout_WhenNotConfigured_Throws()
    {
        var handler = new CreateCheckoutSessionHandler(
            Substitute.For<IBillingRepository>(), Substitute.For<IStripeGateway>(),
            new BillingPlanCatalog(new StripeOptions()), new StripeOptions(), Config());

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.HandleAsync(new CreateCheckoutSessionCommand(Guid.NewGuid(), "Team")));
    }

    [Theory]
    [InlineData("Free")]       // no charge — not a self-serve checkout plan
    [InlineData("Enterprise")] // sales-assisted — no self-serve price
    [InlineData("Nonsense")]   // unknown plan
    public async Task Checkout_ForNonSelfServePlan_Throws(string plan)
    {
        var handler = new CreateCheckoutSessionHandler(
            Substitute.For<IBillingRepository>(), Substitute.For<IStripeGateway>(),
            Catalog, ConfiguredOptions, Config());

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.HandleAsync(new CreateCheckoutSessionCommand(Guid.NewGuid(), plan)));
    }

    // --- Portal ---

    [Fact]
    public async Task Portal_WithoutBillingAccount_Throws()
    {
        var tenantId = Guid.NewGuid();
        var repository = Substitute.For<IBillingRepository>();
        repository.GetByIdAsync(tenantId).Returns(new Tenant { Id = tenantId, StripeCustomerId = null });

        var handler = new CreatePortalSessionHandler(
            repository, Substitute.For<IStripeGateway>(), ConfiguredOptions, Config());

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.HandleAsync(new CreatePortalSessionCommand(tenantId)));
    }
}
