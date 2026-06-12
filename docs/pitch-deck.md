# Pitch Deck: Serto

## 1. Title

**Serto**

Integration-as-Code for teams that want production automation without low-code lock-in.

**Tagline:** Governed integrations from code, running on your infrastructure.

---

## 2. Problem

Companies run critical integrations on a messy mix of cron jobs, scripts, low-code iPaaS tools, enterprise schedulers, and custom internal platforms.

The result:

- Developers own the business logic but lack deployment safety, retries, secrets discipline, audit, and observability.
- Operators own production reliability but cannot safely inspect, version, or change code-driven workflows.
- Low-code platforms become expensive, opaque, hard to test, and hard to version.
- Script-based automation is cheap until it fails invisibly.
- AI can generate integration code faster than ever, but teams still lack a governed way to deploy and operate that code.

---

## 3. Why Now

Three changes make this the right moment:

- **AI-assisted development is mainstream.** Teams will generate more integration code, but generated code still needs deployment, secrets, observability, audit, and rollback.
- **Integration demand keeps growing.** SaaS sprawl, APIs, webhooks, data movement, edge devices, and internal automation all create more glue work.
- **Legacy middleware is misaligned.** Many Boomi/Control-M workloads are really scheduled jobs, API calls, database syncs, file movement, retries, and visibility wrapped in expensive platforms.

The missing layer is not another visual canvas. It is an operational platform that turns real code into safe production automation.

---

## 4. Solution

Serto is a code-first control plane plus runtime agent.

Developers write integrations as normal C# code:

- SDK attributes declare schedules, webhooks, message subscriptions, and runtime requirements.
- Connectors handle common HTTP and SQL work, with file/SFTP and object storage as near-term wedges.
- `serto init`, `serto test`, `serto scan`, `serto package`, and `serto deploy` create a normal local-to-production loop.
- Package upload scans assemblies and auto-provisions integration and trigger records.
- Executions are pinned to package versions, so rollback is an operator action rather than a rebuild.

Operators manage production safely:

- Control plane owns environments, secrets, schedules, triggers, package versions, manual runs, workflows, retries, logs, alerts, audit, RBAC, and billing/licensing.
- Runtime agents execute inside the customer's network, close to private systems and data.
- External-vault mode lets the control plane store secret references only; agents resolve secret values locally.
- Published Docker images support realistic deployment: control plane, external PostgreSQL, and agents on separate hosts.

---

## 5. Product Demo Story

Developer flow:

```bash
dotnet tool install --global Serto.Cli
serto init order-sync
serto test
serto scan
serto package
serto deploy --environment production
```

Control plane result:

- discovered integration classes
- trigger declarations and drift
- required secrets and environment checks
- package version and active pin
- agent status and capability routing
- execution history and searchable logs
- trigger timeline and queued work
- retries, timeouts, and failure alerts
- commercial edition, caps, and license state

---

## 6. Market Wedge

The initial wedge is not "replace every iPaaS workflow."

The wedge is:

**Self-hosted, code-first integration operations for .NET teams replacing cron jobs, brittle scripts, simple Boomi/Control-M workloads, and bespoke internal schedulers.**

Ideal early customers:

- Microsoft/.NET-heavy mid-market companies
- SaaS, logistics, healthcare, fintech, manufacturing, and platform teams with private systems
- Teams with too many cron jobs, scripts, scheduled syncs, and API/database/file workflows
- Security-conscious companies that want integrations to run on their infrastructure
- Consultancies repeatedly building customer integrations

Anti-customer:

- Non-technical teams that need drag-and-drop automation. They should use Zapier, Make, or n8n.

---

## 7. Competitive Positioning

| Alternative | Weakness | Serto Advantage |
|-------------|----------|-----------------|
| Cron/scripts | No governance, retries, audit, deployment, or visibility | Production-grade control plane for real code |
| Boomi/iPaaS | Low-code lock-in, costly, hard to version/test | C# integrations in Git with packages, tests, and rollback |
| Control-M | Heavy scheduler, not developer-native | Modern developer workflow plus agents, triggers, logs, and packages |
| n8n/Zapier/Make | Great for low-code, weaker for complex code-owned workflows | Built for engineering teams that want code and CI/CD |
| Temporal | Powerful durable workflow infrastructure, not an integration product | Higher-level integration control plane, deployment, secrets, agents, and operator UI |
| Hangfire | Good .NET background jobs, narrower operations surface | Distributed integration product with packages, environments, agents, secrets, and audit |
| Internal platform | Expensive to build and maintain | Ready-made integration operations layer |
| AI codegen alone | Produces code, not governed production operations | Generated or human-authored code becomes deployable, observable automation |

---

## 8. Differentiation

Serto combines:

- **Code-first authoring** instead of proprietary workflow lock-in.
- **Self-hosted runtime agents** that run near customer systems and data.
- **Secrets-stay-local architecture** through external-vault reference mode.
- **Version-pinned package execution** with active-version selection and rollback.
- **Trigger-agnostic work queue** for schedules, manual runs, webhooks, workflows, retries, message-triggered integrations, and future file/queue adapters.
- **Operator governance** through RBAC, audit logs, environments, alerts, package history, and billing/licensing.
- **Open developer surface**: SDK, CLI, Connectors, and Testing packages remain MIT-friendly while the control plane is commercially licensed.

---

## 9. Current Progress

Built and validated:

- Multi-tenant control plane and React operator UI
- Runtime agent with polling, package sync, heartbeat/status, and capability tags
- Work-item execution queue with schedules, manual runs, webhooks, workflows, retries, and message-triggered integrations
- Package upload, SHA validation, sync, isolation, version pinning, active version selection, and guarded deletion
- Assembly scanning and package auto-provisioning
- CLI global tool with project scaffolding, local test harness, scan/package/deploy previews, webhook replay, saved credentials, and environment-aware deploys
- Published NuGet packages for SDK, CLI, Connectors, and Testing
- Published Docker deployment model for control plane and agents
- Workflow DAG foundation
- Retry policies, timeouts, orphaned execution reaper, alerts, and health/readiness endpoints
- Execution history, searchable/filterable logs, trigger timeline, package version visibility, and trigger events
- Environments, secrets, external-vault backend, agent-side vault reference resolution
- RBAC, audit logs, invitations, PATs, refresh tokens, password reset, rate limiting
- Stripe billing foundation and self-hosted commercial licensing with signed Ed25519 licenses
- Free Community edition capped by estate size: 10 integrations, 2 environments, unmetered self-hosted executions
- Public landing/install/docs surfaces aligned with the self-hosted integration-as-code positioning
- Automated coverage currently passes across the .NET suite and frontend production build

The project is suitable for paid design-partner pilots and early self-hosted commercial evaluations.

---

## 10. Near-Term Roadmap

Highest-leverage next milestones:

1. **Commercial release hardening**
   - Replace the development license public key, finalize the MIT/commercial license split, and formalize the license issuance workflow.

2. **Operational data growth**
   - Add pagination, execution/log retention cleanup, and log volume controls.

3. **Agent security**
   - Add agent token expiry/rotation and execution-scoped tokens for log/completion calls.

4. **Vault rollout**
   - Add a first-party or documented OpenBao/HashiCorp Vault container path, UI binding mode, and embedded-to-external migration tooling.

5. **Connector wedge**
   - Add File/SFTP, object storage, stronger HTTP pagination/rate-limit helpers, and SQL batching/transactions.

6. **Message-trigger polish**
   - Add publish failure semantics, loop protection, published-message observability, and optional broker-backed delivery later.

7. **AI-assisted authoring**
   - Generate integrations, tests, sample payloads, and required secret manifests once the production loop is reliable enough to govern agent-created code.

---

## 11. Business Model

Recommended model:

- **Community:** free self-hosted edition, full product behind estate-size caps: 10 integrations, 2 environments, unmetered local executions.
- **Commercial self-hosted:** signed offline license lifts caps for Team / Business / Enterprise; no phone-home; expiry degrades after grace rather than bricking production.
- **Managed control plane:** hosted control plane for ops-light teams, with agents still running in the customer's network.
- **Enterprise:** dedicated hosting or self-hosted support, SSO, retention/compliance controls, SLA, and professional services.

Pricing should lean toward predictable subscriptions based on:

- integrations/workflows
- environments
- agents
- retention/compliance requirements
- support level
- hosted versus self-hosted operations

Execution-based pricing should be cloud-only and generous. For self-hosted, estate size is the cleaner commercial signal.

---

## 12. Go-To-Market

Initial GTM should be hands-on and narrow:

1. Identify teams with brittle scheduled/API/database/file workflows.
2. Sell a paid pilot to replace 1-3 real production workflows.
3. Deploy self-hosted or managed-control-plane depending on customer constraints.
4. Measure deployment speed, reliability, observability, rollback, and operator confidence.
5. Turn repeated patterns into templates, connectors, docs, and eventually AI prompts.
6. Expand from one workflow to a team's automation estate.

Best early channels:

- .NET and Microsoft ecosystem
- platform engineering and internal tools communities
- integration consultants and agencies
- founder-led outbound to teams using Boomi/Control-M for simple workflows
- security-conscious mid-market teams that prefer self-hosting

---

## 13. Funding Plan

Do not raise purely on vision if avoidable.

Best sequence:

1. **Commercial polish**
   - Finish the legal/license split, production license key, data retention, token rotation, and connector wedge.

2. **Customer discovery now**
   - 20-30 calls with teams running scheduled syncs, API/database/file workflows, or simple Boomi/Control-M jobs.

3. **Paid pilots**
   - 3-5 paying customers replacing real workflows.

4. **Pre-seed or seed**
   - Raise once there is proof that customers will pay to replace production automation, not just admire the architecture.

Target raise:

- Angel/pre-seed: $250k-$750k if needed to buy time for pilots.
- Seed: $1.5M-$4M after repeatable pilot conversion and a sharper connector wedge.

---

## 14. What Investors Should Believe

The core thesis:

AI will make integration code easier to create, but production automation still needs governance.

The winning platform will not be just a chatbot or another low-code designer. It will be the operational layer that takes generated or human-authored integration code and makes it deployable, observable, retryable, auditable, and safe.

Serto is built for that world, starting with the Microsoft/.NET ecosystem and self-hosted buyers who care about control, security, and avoiding low-code lock-in.

---

## 15. Ask

Near-term ask:

- 3-5 design partners with real scheduled/API/database/file/webhook workflows.
- Paid pilot customers willing to replace one production workflow and measure deployment speed, reliability, observability, and rollback.
- Technical advisors with dev tools, iPaaS, data platform, Microsoft ecosystem, or enterprise automation experience.
- Integration consultants who repeatedly build custom automations for customers.

Funding ask, when traction is proven:

- Capital to accelerate connector depth, production hardening, enterprise security, and go-to-market.

---

## Appendix: Current Product Readiness

Estimated maturity:

- Technical foundation: 80%
- Developer beta readiness: 75%
- Paid pilot readiness: 70-75%
- Revenue product readiness: 55-65%
- Broad self-serve readiness: 50-60%
- Broad Control-M/Boomi replacement: 35-45%

Current best revenue path:

Paid self-hosted pilots, not low-touch SaaS.

Most important remaining gaps:

- Production license key and legal license split
- Data retention, pagination, and log volume controls
- Agent token rotation and execution-scoped tokens
- Vault rollout UX/container/migration path
- File/SFTP and object-storage connector depth
- Message-trigger publish failure semantics and loop protection
