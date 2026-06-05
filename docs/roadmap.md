# Roadmap: The Path to $100M

Product goal: replace legacy integration middleware with a modern, **Integration-as-Code** developer platform.

## Phase 1 — Developer Velocity (Done)
- **Core Connectors:** HTTP and SQL connectors with fluent APIs.
- **SDK NuGet:** Ready-to-use SDK for integration authors.

## Phase 2 — SaaS & Multi-Tenancy (Done)
- **Tenant Registration:** Public self-service onboarding.
- **Quota Enforcer:** Automated monthly execution limits per tenant.
- **Invitations:** Secure team member onboarding.

## Phase 3 — The "Magic" Experience (In Progress)
- **Zero-Touch Provisioning:** Attribute-based discovery and package-upload auto-provisioning.
- **Multi-Trigger Integrations:** Separate integration code from trigger configuration so one integration can have schedules, webhooks, manual runs, and future event triggers.
- **`ip` CLI:** Single tool for `init`, `test`, and `deploy`.
- **Assembly Scanning:** Auto-provisioning integrations and trigger records from code on upload. (Done)
- **Developer Authoring Loop:** Add `ip scan`, `ip package`, deploy previews, local webhook replay, and secret manifests so developers can predict exactly what will be provisioned.
- **Trigger Declarations And Runtime Overrides:** Treat code attributes as trigger intent/defaults while the control plane owns production enablement, secrets, overrides, and drift decisions.
- **Local Dev Tunnel:** Tunnel cloud webhooks to local agents for instant debugging.
- **AI-Assisted Authoring:** Make the SDK AI-legible and generate integrations from natural language, so developers can describe intent and get compiling, testable code.
- **Agent-Operable Control Plane (MCP):** Expose author → deploy → run → observe as a Model Context Protocol server, reusing the existing command/RBAC/audit pipeline, so an AI agent can run the whole loop. Sequence after authoring UX is seamless. See backlog "MCP / Agent-Operable Control Plane."

## Phase 4 — Enterprise Governance (In Progress - Valuation Multipliers)
- **Audit Logs:** Immutable record of all system and secret changes. (Done)
- **RBAC:** Role-Based Access Control (Admin, Developer, Operator). (Done)
- **SSO/SAML:** Integration with Enterprise Identity (Entra ID/Okta).
- **Marketplace Engine:** Support for `ip install connector-x` to build a moat.

## Phase 5 — Workflow Orchestration
- **DAG Foundation:** Workflow definitions, node dependencies, workflow runs, and fan-out/fan-in release semantics.
- **Long-Running Jobs:** Support for `await context.WaitForSignalAsync()`.
- **Approvals:** Manual intervention steps within a code-first workflow.
- **Fan-out/Fan-in:** Complex parallel processing primitives.
