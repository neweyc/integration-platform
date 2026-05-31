# Roadmap

Features are grouped by the phase in which they should be built. Phase 1 completes the MVP. Phases 2–3 enable commercial viability.

---

## Phase 1 — MVP (control plane complete, agent v1)

The control plane is largely feature-complete. The missing piece is the runtime agent.

### Runtime agent
- [ ] Agent process skeleton (console app or worker service)
- [ ] Agent authentication with the control plane using agent tokens
- [ ] Poll endpoint on the control plane: `GET /api/agent/poll`
- [ ] Secret bundle fetch and in-memory injection
- [ ] Integration assembly loading (load a `.dll` from a configured path)
- [ ] Integration execution with `IIntegrationContext`
- [ ] Execution result reporting: `POST /api/agent/executions`
- [ ] Graceful shutdown and cancellation

### Control plane additions for agent support
- [ ] `GET /api/agent/poll` — returns integrations due to run
- [ ] `POST /api/agent/executions` — records execution results
- [ ] Execution history table and EF migration
- [ ] Execution history list endpoint

### UI additions
- [ ] Execution history view per integration
- [ ] Last run status and timestamp on the integrations list

---

## Phase 2 — Production readiness

### Observability
- [ ] Structured execution logs (agent streams logs to control plane during a run)
- [ ] Execution duration tracking
- [ ] Failed execution alerts (email or webhook notification)
- [ ] Dashboard: recent executions, success/failure rate, average duration

### Integration authoring
- [ ] Integration SDK NuGet package (`IntegrationPlatform.Sdk`)
- [ ] `IIntegration`, `IIntegrationContext`, `ISecretContext` interfaces
- [ ] Example integration project (template)
- [ ] Documentation: writing and deploying your first integration

### Agent improvements
- [ ] Webhook trigger support: agent exposes an HTTP endpoint, control plane proxies or agent registers with an external service
- [ ] Retry policy: configurable retry count and backoff per integration
- [ ] Concurrent execution limit per agent
- [ ] Agent version reporting (for compatibility checking)

### Security
- [ ] Token expiry / rotation support
- [ ] JWT refresh tokens
- [ ] Audit log: who changed what, when
- [ ] Rate limiting on public endpoints

### Operations
- [ ] Health check endpoints (`/healthz`, `/readyz`)
- [ ] Execution history data retention policy (configurable, default 90 days)
- [ ] Database backup guidance

---

## Phase 3 — Commercial features

### Multi-tenancy (SaaS mode)
- [ ] Tenant self-registration (public sign-up)
- [ ] Tenant isolation hardening
- [ ] Per-tenant resource limits (integration count, execution frequency)
- [ ] Tenant admin can invite users via email

### User management
- [ ] User invitation flow
- [ ] Member role enforcement (read-only vs admin)
- [ ] Password reset via email

### Billing integration
- [ ] Usage metering (executions per month, per tenant)
- [ ] Stripe integration for subscription management
- [ ] Billing portal (upgrade, downgrade, cancel)
- [ ] Usage dashboard (current period execution count vs plan limit)

### Environments
- [ ] Formal environment model (create/delete environments per tenant)
- [ ] Environment promotion: copy secrets from staging → production

### Integration management
- [ ] Integration versioning (record which assembly version ran)
- [ ] Rollback to a previous version
- [ ] Staging/production promotion workflow

### Marketplace (longer term)
- [ ] Public integration library: pre-built integrations for common systems
- [ ] Integration packaging and publishing
- [ ] One-click install from the marketplace

---

## Known technical debt

- Secrets page is hardcoded to `production` environment — needs an environment selector
- No pagination on list endpoints — will become a problem at scale
- No input sanitisation beyond basic validation — add a global sanitisation layer
- Tests don't cover the agent token endpoints yet
- `ICurrentUser` throws if called outside a JWT context — agent endpoints need a separate identity abstraction
