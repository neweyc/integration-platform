using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace Licensing;

// A license token is two base64url segments joined by a dot:  <payload>.<signature>
//
//   payload   = base64url( UTF-8 JSON of LicensePayload )
//   signature = base64url( Ed25519 signature over the ASCII bytes of the payload segment )
//
// The signature covers the exact payload segment that is transmitted, so there is no JSON
// canonicalization concern — the verifier checks the bytes it received, never a re-serialization.
public static class LicenseToken
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Stable, human-readable enum names (e.g. "Business") rather than ordinals, so a token survives
        // any future reordering of the BillingPlan enum.
        Converters = { new JsonStringEnumConverter() }
    };

    // Sign a payload with the vendor's Ed25519 private key (32 raw bytes), producing a license token.
    public static string Sign(LicensePayload payload, ReadOnlySpan<byte> privateKey)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var payloadSegment = Base64Url.Encode(json);
        var signature = Ed25519Sign(Encoding.ASCII.GetBytes(payloadSegment), privateKey);
        return payloadSegment + "." + Base64Url.Encode(signature);
    }

    // Verify a token against the vendor's Ed25519 public key (32 raw bytes) and, on success, return the
    // payload. Returns false for a malformed token or a signature that doesn't match — the caller treats
    // either as "no valid license". Note: expiry is NOT checked here; that is the license service's job
    // (so it can distinguish expired-but-authentic from forged, and apply a grace period).
    public static bool TryVerify(string token, ReadOnlySpan<byte> publicKey, out LicensePayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var parts = token.Trim().Split('.');
        if (parts.Length != 2)
            return false;

        try
        {
            var payloadSegment = parts[0];
            var signature = Base64Url.Decode(parts[1]);
            if (!Ed25519Verify(Encoding.ASCII.GetBytes(payloadSegment), signature, publicKey))
                return false;

            var json = Base64Url.Decode(payloadSegment);
            payload = JsonSerializer.Deserialize<LicensePayload>(json, JsonOptions);
            return payload is not null;
        }
        catch
        {
            // Any malformed base64/JSON is just an invalid token, not an exception the caller should see.
            return false;
        }
    }

    private static byte[] Ed25519Sign(byte[] message, ReadOnlySpan<byte> privateKey)
    {
        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(privateKey.ToArray(), 0));
        signer.BlockUpdate(message, 0, message.Length);
        return signer.GenerateSignature();
    }

    private static bool Ed25519Verify(byte[] message, byte[] signature, ReadOnlySpan<byte> publicKey)
    {
        var verifier = new Ed25519Signer();
        verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(publicKey.ToArray(), 0));
        verifier.BlockUpdate(message, 0, message.Length);
        return verifier.VerifySignature(signature);
    }
}

// Generates Ed25519 keypairs for the vendor (used by the license tool's keygen). The public key is
// embedded in the control plane; the private key is held only by the vendor.
public static class Ed25519Keys
{
    public static (byte[] PublicKey, byte[] PrivateKey) Generate()
    {
        var random = new SecureRandom();
        var privateKey = new Ed25519PrivateKeyParameters(random);
        var publicKey = privateKey.GeneratePublicKey();
        return (publicKey.GetEncoded(), privateKey.GetEncoded());
    }
}

// URL-safe base64 without padding — keeps tokens free of '+', '/', and '=' so they're easy to paste into
// config files and environment variables.
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded
        };
        return Convert.FromBase64String(padded);
    }
}
