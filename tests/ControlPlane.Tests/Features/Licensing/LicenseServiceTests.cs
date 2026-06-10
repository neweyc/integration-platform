using ControlPlane.Features.Billing;
using ControlPlane.Features.Licensing;
using Licensing;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Licensing;

public class LicenseServiceTests
{
    private static readonly DateTime Expiry = new(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly byte[] _publicKey;
    private readonly byte[] _privateKey;

    public LicenseServiceTests()
    {
        (_publicKey, _privateKey) = Ed25519Keys.Generate();
    }

    [Fact]
    public void NoLicense_IsUnlicensed_AndLeavesPlanUntouched()
    {
        var service = Build(token: null, now: Expiry.AddYears(-1));

        Assert.Equal(LicenseState.Unlicensed, service.Current.State);
        Assert.Equal(BillingPlan.Free, service.EffectivePlanFor(BillingPlan.Free));
    }

    [Fact]
    public void ValidLicense_LiftsTheTenantToTheLicensedPlan()
    {
        var service = Build(BusinessToken(), now: Expiry.AddDays(-30));

        Assert.Equal(LicenseState.Valid, service.Current.State);
        Assert.Equal("Acme Corp", service.Current.Licensee);
        Assert.Equal(BillingPlan.Business, service.EffectivePlanFor(BillingPlan.Free));
    }

    [Fact]
    public void WithinGracePeriod_StillLiftsThePlan()
    {
        // 5 days past expiry, default 14-day grace.
        var service = Build(BusinessToken(), now: Expiry.AddDays(5));

        Assert.Equal(LicenseState.Grace, service.Current.State);
        Assert.Equal(BillingPlan.Business, service.EffectivePlanFor(BillingPlan.Free));
    }

    [Fact]
    public void BeyondGracePeriod_DegradesToCommunity()
    {
        // 30 days past expiry, beyond the 14-day grace.
        var service = Build(BusinessToken(), now: Expiry.AddDays(30));

        Assert.Equal(LicenseState.Expired, service.Current.State);
        Assert.Equal(BillingPlan.Free, service.EffectivePlanFor(BillingPlan.Free));
    }

    [Fact]
    public void TamperedToken_IsInvalid_AndDoesNotLiftThePlan()
    {
        var token = BusinessToken();
        var tampered = token[..^4] + "AAAA"; // corrupt the signature tail

        var service = Build(tampered, now: Expiry.AddDays(-30));

        Assert.Equal(LicenseState.Invalid, service.Current.State);
        Assert.Equal(BillingPlan.Free, service.EffectivePlanFor(BillingPlan.Free));
    }

    [Fact]
    public void WhenStripeConfigured_LicenseIsIgnored()
    {
        // Cloud: Stripe governs plans, so even a valid instance license must not override the tenant's plan.
        var service = Build(BusinessToken(), now: Expiry.AddDays(-30), stripeConfigured: true);

        Assert.Equal(BillingPlan.Team, service.EffectivePlanFor(BillingPlan.Team));
    }

    [Fact]
    public void EffectivePlan_TakesTheHigherOfLicenseAndTenant()
    {
        var service = Build(BusinessToken(), now: Expiry.AddDays(-30));

        // Tenant already higher than the license — keep the higher one.
        Assert.Equal(BillingPlan.Enterprise, service.EffectivePlanFor(BillingPlan.Enterprise));
    }

    private string BusinessToken()
    {
        var payload = new LicensePayload("Acme Corp", BillingPlan.Business, Expiry.AddYears(-1), Expiry);
        return LicenseToken.Sign(payload, _privateKey);
    }

    private LicenseService Build(string? token, DateTime now, bool stripeConfigured = false)
    {
        var options = new LicenseOptions
        {
            Key = token,
            PublicKeyOverride = Base64Url.Encode(_publicKey)
        };
        var stripe = new StripeOptions { SecretKey = stripeConfigured ? "sk_test_123" : null };
        return new LicenseService(options, stripe, NullLogger<LicenseService>.Instance, new FixedTimeProvider(now));
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }
}
