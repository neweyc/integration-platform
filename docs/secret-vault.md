# Design: On-Prem Secret Vault (reference-based secrets)

**Status:** Draft / proposal (not yet scheduled)
**Related:** `docs/cloud-strategy.md`, `docs/architecture.md`, `docs/functional-operations.md`

## Decision

**We will not store secret values in an off-prem (hosted) control plane — full stop.** The hosted
control plane stores only *references* to secrets; the actual secret material lives in a vault that
runs on the customer's own infrastructure (a container on their iron, alongside the runtime agent).
This is the prerequisite that makes a cloud offering adoptable by security-conscious buyers whose
policy is "credentials never leave our network."

## Current state

- Secrets are stored **encrypted in the control plane's Postgres** (`Secret.EncryptedValue`), encrypted
  with `Encryption:MasterKey` — which the control plane also holds.
- `SetSecret` encrypts and stores the value; the agent fetches a **decrypted bundle** via
  `GET /api/agent/secrets/{environment}` (`GetSecretBundle` decrypts and returns plaintext over the wire).
- So today the control plane holds both the ciphertext *and* the key, and hands the agent plaintext.
  Fine for self-hosted; a non-starter for a hosted control plane under "no credentials off-prem."

## Target architecture

- **Store-of-record moves to the vault.** The control plane stores `{ environment, key } → reference`
  (an opaque handle/path into the vault), never the value.
- **The vault runs on the customer's iron** — a container deployed inside the customer's network,
  next to the agent. It can be a first-party lightweight vault or an integration with an existing one
  (OpenBao / HashiCorp Vault / a cloud KMS).
- **The agent resolves references at run time** against the vault in its own network, replacing today's
  "control plane delivers a decrypted bundle." The hosted control plane is never in the path of secret
  material.
- **Self-hosted** can run the same way (control plane + vault as separate containers, keeping values out
  of the application DB) or keep the simple embedded store for trivial setups — see backends.

## Pluggable secret backends

Introduce an `ISecretBackend` abstraction so the model is configurable and migration is clean:

- **Embedded (today's behavior, default):** values encrypted in the control-plane DB. Fine for simple
  self-hosted; kept for backward compatibility.
- **External vault (required for cloud):** the control plane stores references; values live in the
  on-prem vault; the agent resolves them locally. **Hosted deployments mandate this backend.**

This lets cloud enforce "no secrets here" while self-hosted picks the trade-off it wants.

## Where secret values are written

The sharpest design question: the value must reach the vault without resting in — ideally without even
transiting — the hosted control plane.

- **Preferred:** secret *values* are written **directly to the vault** (its own API/CLI/UI, reachable in
  the customer network); the control plane records only the reference/binding and never sees the value.
  Cleanest security story; slightly more UX (managing a binding vs typing a value).
- **Weaker alternative:** the control plane proxies the write straight to the vault without persisting.
  Simpler UX, but the value transits the cloud in memory — strict policies may still object. Acceptable
  for self-hosted/embedded; avoid for the strict hosted buyer.

Under the external backend the Secrets page becomes a manager of **bindings** (which key maps to which
vault reference) rather than a store of values.

## Implications

- Cloud becomes adoptable by buyers who forbid off-prem credentials — the make-or-break for the hosted
  tier (see `cloud-strategy.md`).
- The agent's secret contract changes from "decrypted bundle from the control plane" to "references from
  the control plane + values from the vault."
- `Encryption:MasterKey` responsibility moves to the vault under the external backend; the control plane
  no longer needs the master key in that mode.

## Open questions

1. **Build vs integrate** — a first-party lightweight vault (simplest to deploy) vs integrating
   OpenBao/HashiCorp Vault/cloud KMS first (faster, more trusted). Likely integrate first.
2. Reference format, and how environments and rotation map onto vault paths.
3. UX for value entry under the external backend (direct-to-vault vs proxied), per the posture above.
4. Migration tooling: embedded → external (move values into the vault, replace with references).
5. Trust model: how the vault authenticates the agent and the control plane (the agent already carries
   an agent token; the vault needs its own).

## Rollout

1. `ISecretBackend` abstraction; today's DB store becomes the **embedded** backend (no behavior change).
2. **External-vault** backend: reference storage in the control plane + agent-side resolution.
3. Vault container (first-party or integration) added to the compose/agent stack.
4. Secrets UI binding-management mode; migration tooling.
5. Cloud mandates the external backend.
