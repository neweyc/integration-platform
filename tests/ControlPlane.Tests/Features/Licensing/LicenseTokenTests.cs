using Licensing;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Licensing;

public class LicenseTokenTests
{
    [Fact]
    public void SignThenVerify_Roundtrips()
    {
        var (publicKey, privateKey) = Ed25519Keys.Generate();
        var payload = new LicensePayload("Acme Corp", BillingPlan.Business,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), MaxTenants: 1);

        var token = LicenseToken.Sign(payload, privateKey);
        var verified = LicenseToken.TryVerify(token, publicKey, out var decoded);

        Assert.True(verified);
        Assert.Equal("Acme Corp", decoded!.Licensee);
        Assert.Equal(BillingPlan.Business, decoded.Plan);
        Assert.Equal(payload.Expiry, decoded.Expiry);
        Assert.Equal(1, decoded.MaxTenants);
    }

    [Fact]
    public void Verify_WithWrongPublicKey_Fails()
    {
        var (_, privateKey) = Ed25519Keys.Generate();
        var (otherPublic, _) = Ed25519Keys.Generate();
        var token = LicenseToken.Sign(Payload(), privateKey);

        Assert.False(LicenseToken.TryVerify(token, otherPublic, out _));
    }

    [Fact]
    public void Verify_TamperedPayload_Fails()
    {
        var (publicKey, privateKey) = Ed25519Keys.Generate();
        var token = LicenseToken.Sign(Payload(), privateKey);

        // Flip the payload segment to a different (validly-encoded) payload, keeping the original signature.
        var forgedPayload = Base64Url.Encode(
            System.Text.Encoding.UTF8.GetBytes("""{"Licensee":"Evil","Plan":"Enterprise","IssuedAt":"2026-01-01T00:00:00Z","Expiry":"2099-01-01T00:00:00Z"}"""));
        var tampered = forgedPayload + "." + token.Split('.')[1];

        Assert.False(LicenseToken.TryVerify(tampered, publicKey, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("only.one.too.many")]
    public void Verify_MalformedToken_FailsGracefully(string token)
    {
        var (publicKey, _) = Ed25519Keys.Generate();
        Assert.False(LicenseToken.TryVerify(token, publicKey, out _));
    }

    private static LicensePayload Payload() => new("Acme", BillingPlan.Team,
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
}
