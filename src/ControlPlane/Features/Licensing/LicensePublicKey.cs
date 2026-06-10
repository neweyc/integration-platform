namespace ControlPlane.Features.Licensing;

// The vendor's Ed25519 public key, shipped with the control plane to verify commercial license tokens
// offline (no phone-home). The matching private key is held only by the vendor and used by
// tools/LicenseTool to sign licenses.
//
// NOTE: this is a DEVELOPMENT placeholder keypair. Before distributing a real build, run
// `serto-license keygen`, keep the private key secret, and replace this constant with the printed public
// key. Rotating the signing key means updating this constant and re-issuing licenses. See docs/licensing.md.
public static class LicensePublicKey
{
    public const string Base64 = "UbKowd9olpFtht0MZbn9Gf0CdwbD3OsWMlLKnTJtevc";
}
