# Cloud Offering: Strategy & Prerequisites

**Status:** Draft / direction
**Related:** `docs/secret-vault.md`, `docs/licensing.md`, `docs/monetization.md`, `docs/architecture.md`

## The honest tension

The runtime agent runs in the customer's network either way, so hosting only moves the **control plane**
(API/UI/Postgres/scheduler) to the vendor. For a mid-sized company with an ops team, "we'll run a
Postgres and a web app for you" is a thin value prop — and it adds a hard objection: the control plane
(with secrets, schedules, audit) would live in someone else's cloud. So the hosted tier is weak *as
framed* ("we run the infra for you"), especially for the security-conscious mid-market.

## Who hosted is actually for

- **Not** the ops-heavy mid-market — they self-host and buy a commercial license (see `licensing.md`).
- **Dev-heavy but ops-light** teams: small SaaS startups, agencies, platform teams of 3–5 engineers who
  want integration-as-code but won't run Postgres + a control plane. Real, but a *secondary* market.
- Pure no-ops / no-dev teams go to Zapier/Make and never evaluate a code-first tool — not our buyer.

## Use cases that favor a central (not necessarily hosted) control plane

- **Geo-separated entities / multi-site.** A signal on a factory floor in Chicago triggering a
  supply-chain flow in Dallas is exactly the trigger → workflow → capability-routing stack at work
  (a capability-tagged agent per site, a central control plane coordinating). This favors *centralizing*
  the control plane; it only favors *vendor-hosting* for customers who can't stand up that central,
  reachable node themselves.

## The binding constraint: secrets

"Credentials cannot leave our network" is a common, often hard policy (finance, healthcare, and plenty
of ordinary security teams). Today the control plane **is** the secret store and hands the agent
plaintext, so a hosted control plane is a non-starter for that buyer.

**Decision: no secrets in the cloud, full stop.** The hosted control plane stores only references; secret
material lives in an on-prem vault the customer runs (a container on their iron). This is the
make-or-break for cloud — not "managed Postgres." See `docs/secret-vault.md`.

## Sequencing

1. **Lead with commercial self-hosted licensing** (`licensing.md`) — the strongest, lowest-friction bet
   for the stated ICP (mid-market, security-conscious). The secrets concern is a *feature* of
   self-hosted, not a bug.
2. **Cloud is phase-2**, gated on the external secret-vault backend. Don't invest heavily in hosting
   until secrets-stay-on-prem is built, or the buyers we want can't legally adopt it.
3. If cloud is wanted sooner, target the **dev-heavy/ops-light** segment, where the secrets restriction
   usually doesn't apply.
