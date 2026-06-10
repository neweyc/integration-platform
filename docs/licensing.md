# Design: Commercial Licensing & Editions

**Status:** Draft / proposal (not yet scheduled)
**Related:** `docs/monetization.md`, the billing/plan machinery (`BillingPlanCatalog`, `Tenant.Plan`), `docs/installation.md`

## Problem

Self-hosted deployments have no upgrade path: the execution quota (`Tenant.MaxExecutionsPerMonth`,
default 1000) is only ever raised by the Stripe subscription webhook, which is inert on-prem. So today
a self-hosted business that needs more is stuck (or edits the database), and a hobbyist is throttled at
1000 — contradicting the strategy that self-hosted should be free for casual use. We want: **casual users
free, professional users pay, and a trial that's fully representative of the paid product.**

## Principles

- **Cap, don't cripple.** Community is the *full* product, bounded only by the size of the estate. A
  trial is representative because nothing is feature-gated — you only meet a ceiling at business scale.
- **Gate on estate size, not executions (self-hosted).** Cap **integrations** and **environments**; do
  not meter executions on-prem. Cloud keeps execution metering, where it maps to real cost we bear.
- **A license is a compliance instrument, not DRM.** The paying segment pays because unlicensed
  commercial use is a liability (procurement, audit, legal), not because a bypass is hard. Keep the
  honest honest; don't build a cage that only annoys legitimate customers.

## Editions

| Edition | How | Caps | Support |
|--------|-----|------|---------|
| **Community** | Free, no license (self-hosted) | 10 integrations, 2 environments, single tenant | Community |
| **Commercial** (Team / Business / Enterprise) | Signed license key (self-hosted) | Raised/unlimited per plan; enterprise features (SSO, retention, RBAC depth) at the top tiers | Paid, with SLA |
| **Cloud-hosted** | Per-tenant Stripe subscription | Per-plan; **execution-metered** (cost-based) | Per plan |

The integration cap (**10**, ratified 2026-06-10) is the primary hobbyist/business dial and is trivially
tunable via `BillingPlanCatalog.MaxIntegrationsFor`.

## Mechanism: a license key just sets the Plan

The control plane already maps `Tenant.Plan` → limits via `BillingPlanCatalog`. A license does on-prem
exactly what the Stripe webhook does in cloud: **it sets the Plan**, and the existing per-plan limits
apply. Minimal new surface.

- **Signed, offline license file.** Encodes `{ licensee, plan, expiry, optional maxTenants }`, signed
  with the vendor's private key; the control plane ships the public key and validates at startup. **No
  phone-home** — air-gapped/corporate buyers often forbid outbound calls, and offline signing is enough
  for compliance.
- **Instance-level entitlement.** On-prem is almost always single-tenant, so the license entitles the
  *deployment* and applies its plan to the tenant(s). Cloud stays per-tenant via Stripe. Same `Plan`
  field, two sources.
- **No license = Community** (the Free plan's caps).
- **Expiry degrades, never bricks.** On expiry: a grace period with loud warnings, then drop to
  Community caps. Never hard-stop a running production system — that loses the renewal and the reference.

## What gets capped, and where it's enforced

| Limit | Community | Enforced at |
|-------|-----------|-------------|
| Integrations | 10 (`MaxIntegrationsFor(plan)`) ✅ | `CreateIntegration` **and** package-upload provisioning — block only **net-new** integrations beyond the cap; redeploys of existing ones always succeed. *(Shipped.)* |
| Environments | 2 (already shipped) | `CreateEnvironment` (done) |
| Executions | not gated on-prem ✅ | metering kept **cloud-only** (`QuotaService` returns unmetered when Stripe is unconfigured); the hard 1000 cap no longer applies self-hosted. *(Shipped.)* |

## Control-plane license (ratified 2026-06-10)

For "unlicensed commercial use is a liability" to be true, the enforcing component can't be MIT. **Decision:**

- Keep **SDK / CLI / Connectors / Testing MIT** — the integration-as-code promise stays open, authors'
  projects reference open packages.
- Make the **control plane source-available / commercial** (not MIT). This is the upstream decision the
  whole licensing lever rests on; it is now **ratified**, unblocking the signed-license-key work (step 2).
  The repository's license headers / `LICENSE` files still need to be updated to reflect this split.

## Trial

- **Forever-free Community (capped) is the default free path** — not a countdown. Developer-led adoption
  is the wedge; trialware that dies in 30 days kills the grassroots/internal-champion motion.
- **Optional time-limited full-scale trial license** for formal evaluations: the real product at real
  scale for ~30 days, then it lapses to Community caps (not a dead app).

## License token (Ed25519, shipped 2026-06-10)

The signing scheme is **Ed25519** (open question 2, resolved). A license is a self-contained token of two
URL-safe-base64 segments joined by a dot:

```
<payload>.<signature>     payload = base64url(UTF-8 JSON of { Licensee, Plan, IssuedAt, Expiry, MaxTenants? })
                          signature = base64url(Ed25519 signature over the payload segment bytes)
```

The signature covers the exact payload segment transmitted, so there is no JSON-canonicalization concern.
The format + crypto live in the `Licensing` project (`LicenseToken`, `Ed25519Keys`), referenced by the
control plane (verify) and the vendor tool (sign) — deliberately **not** by the MIT SDK/CLI/agent, so
BouncyCastle stays off the customer-facing path.

**Control plane (verify + apply).** `LicenseService` loads the token from `License:Key` (inline) or
`License:FilePath` once at startup, verifies it against the embedded public key (`LicensePublicKey`), and
exposes `EffectivePlanFor(tenantPlan)` — evaluated **live** so expiry/grace degrade without a restart:

- *Unlicensed* (no token) → Community caps. *Invalid* (bad signature) → Community caps + a loud error log.
- *Valid* / *Grace* (within `License:GraceDays`, default 14, past expiry) → lifted to the licensed plan.
- *Expired* (beyond grace) → degrades to Community. On cloud (Stripe configured) the license is ignored.

`GET /api/license` reports the current edition/state/expiry plus the caps the edition entitles, surfaced
on the Billing page (the License/Edition card) for self-hosted deployments.

**Vendor tool.** `tools/LicenseTool` (`serto-license`, vendor-internal, not shipped):

```
serto-license keygen --out ./keys                 # Ed25519 keypair; embed the public key in LicensePublicKey
serto-license issue  --key ./keys/license-signing.key \
                     --licensee "Acme Corp" --plan Business --expires 2027-01-01 [--max-tenants 1]
```

> The public key currently embedded in `LicensePublicKey` is a **development placeholder**. Before
> distributing a real build, run `keygen`, keep the private key secret, and replace the constant.

## Open questions

1. ~~Integration cap number~~ — **resolved: 10** (2026-06-10).
2. ~~License format & signing~~ — **resolved: Ed25519**, self-contained token, `Licensing` project (2026-06-10).
3. Per-instance vs per-tenant on the rare multi-tenant on-prem deployment — default **instance-level**.
4. Which capabilities are *feature-gated* (truly enterprise-only, e.g. SSO) vs *cap-gated*. Prefer caps +
   support as the primary gate so trials stay representative; feature-gate sparingly.

## Rollout

1. ✅ **Done (2026-06-10)** — `MaxIntegrationsFor(plan)` on `BillingPlanCatalog` (Community = 10); enforced
   on `CreateIntegration` and package-upload provisioning (net-new only). Self-hosted execution cap relaxed
   (`QuotaService` meters only when Stripe is configured).
2. ✅ **Done (2026-06-10)** — Ed25519 signed license token: `Licensing` project (format + sign/verify),
   `LicenseService` (startup load + live `EffectivePlanFor` with grace/degrade), threaded into the cap
   enforcers, `GET /api/license`, and the `serto-license` issuance tool. Public key embedded
   (`LicensePublicKey`, currently a dev placeholder).
3. ✅ **Done (2026-06-10)** — Surface edition/expiry/caps in the UI (Billing page License/Edition card,
   self-hosted) backed by an enriched `GET /api/license`; graceful expiry (grace → degrade) is live via
   `EffectivePlanFor`.
4. Update `monetization.md` tiers (done); build out the vendor-side key-issuance workflow. **Also: update
   the repo `LICENSE` files to reflect the MIT-SDK / commercial-control-plane split**, and replace the
   embedded dev-placeholder public key with a production key.
