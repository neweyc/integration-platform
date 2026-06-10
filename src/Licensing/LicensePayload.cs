using Shared.Domain;

namespace Licensing;

// The signed contents of a commercial license. Instance-level: it entitles a whole self-hosted
// deployment, not a single tenant. Signed offline with the vendor's Ed25519 private key and verified by
// the control plane against the shipped public key — no phone-home. See docs/licensing.md.
public record LicensePayload(
    // Who the license is issued to (organization name); shown in the UI and used for support/compliance.
    string Licensee,

    // The plan this license entitles. The control plane applies the matching per-plan caps
    // (BillingPlanCatalog) while the license is valid.
    BillingPlan Plan,

    // When the license was issued (UTC) and when it expires (UTC). After expiry the deployment enters a
    // grace period and then degrades to Community caps — it never bricks. Perpetual licenses set a
    // far-future expiry.
    DateTime IssuedAt,
    DateTime Expiry,

    // Optional cap on the number of tenants the license entitles (on-prem is usually single-tenant).
    int? MaxTenants = null);
