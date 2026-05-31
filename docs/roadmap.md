# Roadmap

Features are grouped by the phase in which they should be built. Phase 1 is largely complete. Phases 2–3 enable commercial viability.

---

## Phase 1 — MVP (largely complete)

### Runtime agent (done)
- [x] Agent process skeleton (Worker service)
- [x] Agent authentication with the control plane using agent tokens
- [x] Poll endpoint on the control plane: `GET /api/agent/integrations`
- [x] Secret bundle fetch and in-memory injection
- [x] Integration assembly loading (load a `.dll` from a configured path)
- [x] Integration execution with `IIntegrationContext`
- [x] Execution result reporting: `POST /api/agent/executions`, `PUT /api/agent/executions/{id}`
- [x] Concurrency limits (`MaxConcurrentExecutions`)
- [x] In-flight tracking to prevent overlapping executions
- [ ] Graceful shutdown and cancellation (partial)

### Control plane (done)
- [x] `GET /api/agent/integrations` — returns enabled integrations for an environment
- [x] `POST /api/agent/executions` — opens execution record with validation
- [x] `PUT /api/agent/executions/{id}` — closes execution with result
- [x] Execution history table and EF migration
- [x] Tenant/environment/enabled validation on execution start
- [x] Integration package storage endpoints

### UI (done)
- [x] Integration list with environment, trigger, status
- [x] Create integration form (name, slug, environment, trigger, cron, className)
- [x] Edit integration form (name, description, status, cron)
- [x] Delete integration
- [x] Execution history view per integration
- [x] Last run status and timestamp on the integrations list

### Remaining for MVP
- [x] Execution history UI in control plane
- [x] Structured logging from agent to control plane
- [ ] Durable scheduling state (currently in-memory only)

---

## Phase 2 — Production readiness

### Observability
- [ ] Structured execution logs (agent streams logs to control plane during a run)
- [ ] Execution duration tracking
- [ ] Failed execution alerts (email or webhook notification)
- [ ] Dashboard: recent executions, success/failure rate, average duration

### Integration authoring
- [ ] Integration SDK NuGet package (`IntegrationPlatform.Sdk`)
- [ ] Example integration project (template)
- [x] Documentation: writing and deploying your first integration

### Agent improvements
- [ ] Webhook trigger support: agent exposes an HTTP endpoint or control plane proxies
- [ ] Retry policy: configurable retry count and backoff per integration
- [ ] Agent version reporting (for compatibility checking)
- [ ] Durable scheduling with distributed locking (for multi-agent deployments)
- [ ] Graceful shutdown with execution draining

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
- [ ] Agent package sync from control-plane package storage
- [ ] Integration versioning (record which assembly/package version ran)
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
- Scheduling state is in-memory only — agent restart re-evaluates all cron expressions
- `ICurrentUser` throws if called outside a JWT context — agent endpoints use separate token validation
