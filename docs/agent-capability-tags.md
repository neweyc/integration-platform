# Design: Agent Capability Tags

**Status:** Draft / proposal (not yet scheduled)
**Author:** —
**Related:** `docs/architecture.md`, the trigger declared-defaults pattern, the open "Authz Revisit" item

## Problem

Today every runtime agent in an environment is interchangeable: the control plane routes
work purely by environment, and any connected agent can claim any work item for that
environment. That breaks down when agents are **not** interchangeable:

- A machine wired to physical hardware must run the integration that signals that hardware —
  no other agent can.
- Some agents have network reach (a VPN into a customer subnet), a GPU, a licensed driver,
  or local tooling that others lack.

We need a way to say "this integration can only run on an agent that offers capability X,"
and to route accordingly.

## Goals

- Let an agent advertise the **capabilities it offers** (free-form tags).
- Let an integration declare the **capabilities it requires**.
- Route a work item only to an agent whose tags satisfy the integration's required tags.
- Preserve today's zero-config behavior: no required tags ⇒ any agent in the environment.
- Make "no eligible agent" a **visible** state, not a silent stall.

## Non-goals

- **Multi-environment agents.** Useful, but it collides with the environment-scoped token /
  secret isolation model and deserves its own design. Out of scope here.
- **Tags as a security/trust boundary.** In this design tags are **routing only** and are
  **self-reported** by the agent. A tag like `pci-host` does **not** grant access to anything;
  it only influences where work runs. If we later need trusted capability assertions, those must
  be server-assigned to the agent token, not self-claimed — see Open Questions.
- Weighting / preference / "best fit" scoring. Matching is boolean.

## Current state (for reference)

- An agent authenticates with `X-Agent-Token`; the control plane resolves the token to a
  tenant + environment (`AgentToken.Environment`, single string) — `AgentTokenEndpoints.cs`.
- `GET /api/agent/integrations` → `PollIntegrationsCommand(TenantId, Environment, LeaseOwnerId)`
  claims due work across the trigger sources via `PollRepository`. Every claim query filters
  `w.Environment == environment` (`PollRepository.cs`).
- `WorkItem` carries `Environment`, `IntegrationId`, claim fields; the lease owner is the
  `AgentToken.Id` (`WorkItem.cs`).
- Agents report liveness via `POST /api/agent/heartbeat` → `AgentHeartbeat`
  (version, hostname, concurrency). No capability info today.
- Integration metadata is declared in code via attributes (`src/Sdk/Attributes.cs`),
  discovered by `AssemblyScanner`, and auto-provisioned on package upload. Trigger fields use a
  **declared-default + override** pattern (`DeclaredCronExpression`/`DeclaredEnabled`): code
  declares intent, an operator may override in the control plane, and redeploys preserve the
  override while reporting drift.

## Proposed design

### 1. Integration declares required capabilities (in code, overridable)

New SDK attribute, consistent with the existing ones:

```csharp
[Integration("Pulse the reactor", "reactor-pulse")]
[RequiresAgentCapabilities("hardware-signal", "site-floor-1")]
public class ReactorPulse : IIntegration { ... }
```

- `AssemblyScanner` extracts the tags into `DiscoveredIntegration.RequiredTags`.
- `Integration` gains `RequiredTags` (active) and `DeclaredRequiredTags` (last code value),
  mirroring the trigger declared-default pattern: an operator can override the required tags in
  the control plane; redeploys keep the override and report drift.
- Empty list = runnable on any agent in the environment (today's behavior).

### 2. Agent advertises offered capabilities

- New `AgentOptions.Tags` (`string[]`), set in `appsettings.json` / env:
  ```json
  "Agent": { "Environment": "production", "Tags": ["hardware-signal", "site-floor-1"] }
  ```
- The agent sends its tags **on every poll** (authoritative for routing) and **on heartbeat**
  (persisted, for the dashboard and unroutable-work detection). Because the poll is a `GET`,
  send them in an `X-Agent-Capabilities` header (comma-separated); switching the poll to `POST`
  with a body is the alternative if lists grow.

### 3. Routing rule

An agent is eligible for a work item iff:

```
workItem.Environment ∈ agent.environments        (today: == agent.environment)
AND  integration.RequiredTags ⊆ agent.Tags        (new; AND/subset semantics)
```

Implementation: each `Claim*` method in `PollRepository` already loads candidate work items
joined to their `Integration`. Add a post-load filter — keep only items whose `RequiredTags` are
all present in the polling agent's tags — before marking them claimed. (Required-tag sets are
small; in-memory subset check over the candidate batch is fine. Postgres array containment
`@>` is an option if we push it into SQL later.)

### 4. Observability: unroutable work

The moment routing can fail to match, work can sit `Pending` forever. We must surface it:

- Persist agent tags on `AgentHeartbeat` (add `Tags`), so the control plane knows the union of
  capabilities currently offered per environment.
- Add a check/endpoint: pending work items whose `RequiredTags` are not covered by **any**
  currently-live agent in their environment → report as "unroutable" in the UI (Integrations /
  Agents page) and as a failure-alert candidate, e.g. *"reactor-pulse needs [hardware-signal];
  no live agent in production offers it."*

## Data model changes

| Entity | Change |
|--------|--------|
| `Integration` | add `RequiredTags string[]`, `DeclaredRequiredTags string[]` |
| `AgentHeartbeat` | add `Tags string[]` (reported each heartbeat) |
| `WorkItem` | none — required tags are resolved via the item's `Integration` at claim time |

One EF migration (Postgres `text[]` columns; default empty array → no drift for existing rows,
mirroring the `AddTriggerDeclaredDefaults` seeding approach).

## Integration points (where the work lands)

- **SDK:** `src/Sdk/Attributes.cs` — new `RequiresAgentCapabilitiesAttribute`.
- **Scan:** `AssemblyScanner` — extract tags into the discovered model; `UploadPackage` upsert
  writes `RequiredTags`/`DeclaredRequiredTags` (with override-preserving + drift reporting).
- **Agent:** `AgentOptions.Tags`; send `X-Agent-Capabilities` on poll and include tags in the
  heartbeat request.
- **Control plane:** poll endpoint reads the capabilities header into
  `PollIntegrationsCommand`; `PollRepository` claim queries gain the subset filter;
  `AgentHeartbeatCommand`/entity gain `Tags`; new unroutable-work signal + UI.

## Backward compatibility

- No required tags on an integration ⇒ matches every agent (current behavior).
- An agent with no configured tags ⇒ can only run integrations that require no tags.
- Existing tokens, agents, and integrations keep working unchanged after migration.

## Open questions

1. **Match semantics:** subset/AND (proposed) vs. "any-of." AND is the predictable default; we
   can add per-tag "any-of" groups later if a real need appears.
2. **Tag governance:** free-form strings vs. a tenant-managed catalog (autocomplete, typo
   protection). Start free-form; revisit if it gets messy.
3. **Trust:** if a capability ever needs to be *trusted* (compliance, data residency), tags must
   become server-assigned to the agent token rather than self-reported. Explicitly deferred; do
   not build authz on self-reported tags. Folds into the "Authz Revisit" pass.
4. **Where operators edit required-tag overrides:** the Integrations edit sheet, alongside the
   existing trigger overrides.
5. **Interaction with multi-environment agents:** the eligibility rule already reads
   `env ∈ agent.environments`; the set-of-environments change is a separate design that this one
   is forward-compatible with.

## Rollout

1. Data model + migration; SDK attribute + scanner extraction (no routing change yet — tags
   recorded but ignored).
2. Agent reports tags (poll + heartbeat); claim filter goes live behind the subset rule.
3. Unroutable-work detection + UI surface + alert.
4. Operator override of required tags in the UI (drift reporting like triggers).
