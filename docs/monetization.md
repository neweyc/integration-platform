# Monetization: Integration-as-Code

## The Category We Own

We are not an IPaaS (Integration Platform as a Service) in the legacy sense (Boomi, MuleSoft). We are a **Developer Integration Platform**.

**We solve the "Low-Code Trap":** Enterprise integrations end up requiring code anyway. Instead of hiding that code in a proprietary black box, we make it the first-class citizen. By treating integrations as **Infrastructure**, we enable:
- **Version Control:** Integrations live in Git.
- **CI/CD:** Automated testing and deployment via the `serto` CLI.
- **Velocity:** Engineering teams build 10x faster using idiomatic C# and our specialized **Enterprise Connectors**.

---

## The problem we solve

Enterprise integration platforms (Dell Boomi, MuleSoft, Workato) are expensive and require specialist skills. Their low-code/no-code interfaces look accessible but hide complexity — non-trivial integrations end up requiring professional services anyway, and you're locked into a proprietary execution model.

Serto targets teams that already write code. It removes the platform tax without removing control.

---

## Target customers

**Primary:** Mid-market software companies (50–500 employees) with:
- An engineering team that writes C#
- Multiple internal systems that need to talk to each other
- Data sync, event-driven workflows, or scheduled batch jobs
- An appetite to own their integrations as code rather than pay for a black box

**Secondary:**
- Agencies and consultancies building custom integrations for clients
- Enterprises that want self-hosted data pipelines without SaaS lock-in

**Anti-customer:** Non-technical teams who need drag-and-drop. Send them to Zapier or Make.

---

## Pricing model

### Tier 1 — Open source / self-hosted (free)

- Full control plane + runtime agent
- Unlimited integrations
- Unlimited executions
- Community support only
- Single tenant per deployment

**Goal:** Build adoption, establish trust, make the product the default choice for developers who want code-first integrations.

---

### Tier 2 — Cloud-hosted ($99–$299/month per tenant)

The control plane is hosted and managed. The runtime agent still runs on-premise inside the customer's network.

| Feature | Included |
|---------|----------|
| Hosted control plane | ✓ |
| Automatic backups | ✓ |
| Uptime SLA | 99.9% |
| Execution history (90 days) | ✓ |
| Email alerts on failure | ✓ |
| Users | 5 seats |
| Executions | 10,000 / month |
| Support | Email, 2 business day response |

Overage: $0.01 per execution above the monthly limit.

**Why customers pay:** They don't want to run and maintain PostgreSQL and the control plane. They want the agent inside their network but without the ops burden of the rest.

---

### Tier 3 — Business ($699+/month)

For teams with higher volume or compliance requirements.

| Feature | Included |
|---------|----------|
| Everything in Tier 2 | ✓ |
| Unlimited users | ✓ |
| Executions | 100,000 / month |
| Audit log | ✓ |
| Role-based access control | ✓ |
| Custom retention period | ✓ |
| SSO (SAML/OIDC) | ✓ |
| Support | Priority, 4-hour response |

---

### Tier 4 — Enterprise (custom)

- Volume discounts
- Dedicated infrastructure (VPC, private link)
- Custom SLA
- On-site professional services / integration development
- Annual contract with invoicing

---

## Go-to-market

### Phase 1: Developer adoption (open source)

- Release on GitHub with a permissive licence (MIT or Apache 2)
- Write content targeting searches like "code-first integration platform", "alternative to Boomi", "C# integration framework"
- Post in .NET / C# communities (r/dotnet, dev.to, LinkedIn)
- Make it trivially easy to run locally (`docker-compose up`)
- Target integration-heavy verticals: e-commerce, logistics, healthcare, fintech

### Phase 2: Convert self-hosters to cloud

- In-app prompt when execution volume or complexity grows: "Want us to manage this for you?"
- Trial offer: 30-day free cloud trial
- Migration tooling: export config from self-hosted, import to cloud

### Phase 3: Enterprise sales

- Outbound to mid-market companies known to use Boomi/MuleSoft (LinkedIn, intent data)
- Position on total cost of ownership: "Replace your Boomi subscription with developer tooling you control"
- Partner with .NET consultancies who already do integration work for clients

---

## Competitive positioning

| | Serto | Dell Boomi | Zapier | Custom code |
|--|--|--|--|--|
| Skill required | C# developer | Specialist | Non-technical | Developer |
| Integration as code | ✓ | ✗ | ✗ | ✓ |
| Self-hosted | ✓ | ✗ | ✗ | ✓ |
| On-premise data access | ✓ | ✓ (agent) | ✗ | ✓ |
| Platform lock-in | Low | High | High | None |
| Cost (mid-market) | $99–$299/mo | $20k+/yr | $400–$800/mo | Eng time |
| Time to first integration | Hours | Days | Minutes | Days–weeks |

---

## Unit economics (illustrative)

| Metric | Target |
|--------|--------|
| Monthly churn | < 3% |
| Average contract value (Tier 2) | $180/mo |
| Average contract value (Tier 3) | $800/mo |
| CAC (inbound/content) | < $500 |
| CAC (outbound/sales) | < $2,000 |
| Gross margin (cloud tiers) | ~80% |
| Payback period | < 3 months |

At 100 Tier 2 customers and 20 Tier 3 customers: ~$34k MRR (~$408k ARR). A relatively small number of enterprise contracts at $3–5k/month would change the economics significantly.

---

## Risks

**Risk: Developers prefer to write custom code**
Mitigation: Position as infrastructure, not a replacement for writing code. You still write C# — the platform handles scheduling, secrets, observability, and retries.

**Risk: Market is dominated by established players**
Mitigation: Target the underserved segment of teams who find Boomi too expensive and opaque but don't want the maintenance burden of a bespoke solution.

**Risk: Open source gives away too much**
Mitigation: The value of the cloud tier is operations (hosting, backups, monitoring), not features. Common pattern (HashiCorp, GitLab). Keep advanced enterprise features cloud-only.

**Risk: .NET-only limits addressable market**
Mitigation: The agent SDK can be extended to other runtimes (Python, Node) without changing the control plane. Address this in Phase 3 once C# traction is proven.

---

## Progress & Commercial Viability

As of Phase 2, the following commercial enablers are **Live**:
- **Multi-Tenant SaaS Foundation:** Public self-registration for new tenants.
- **Quota & Metering:** Automated monthly execution limits are enforced per tenant.
- **Team Collaboration:** Invitation system for onboarding members.
- **Developer Productivity:** Core Connectors (HTTP/SQL) make the platform significantly faster to adopt than raw code or low-code alternatives.
- **Stripe Foundation:** Tenant model is prepared for Stripe Customer and Subscription ID integration.

---

## Strategic Goals for Buyout (Multi-Million Dollar Exit)

To achieve a **$10M–$100M+ exit**, we must demonstrate:
1.  **Workflow Moat:** Prove that once a team adopts our SDK and CLI, the "Integration-as-Code" workflow is too valuable to replace.
2.  **Connector Ecosystem:** Build a marketplace of high-value "Enterprise Wedges" (e.g., SAP, Salesforce, Oracle) that simplify the hardest integration problems.
3.  **Low-Friction Adoption:** High "Self-Serve" conversion rates from `serto init` to first paid quota hit.
4.  **Operational Maturity:** Enterprise-grade Audit Logs, RBAC, and Compliance reporting.
5.  **Platform Stickiness:** Native integration with Azure, GitHub, and Entra ID (making us the default choice for the Microsoft ecosystem).
