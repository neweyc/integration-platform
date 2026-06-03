# Correctness Controls

Correctness is enforced through repeatable gates, not trust in a single implementer.

## Required Gates

Every production change should pass:

- `git diff --check`
- `.NET restore`
- `.NET build with warnings as errors`
- Full `.NET test` suite
- API contract tests for client-facing response shapes
- Tenant-isolation and agent-token misuse tests
- Frontend `npm run lint`
- Frontend `npm run build`
- PostgreSQL-backed integration tests with `INTEGRATION_TEST_CONNECTION` set

The local command is:

```bash
scripts/validate.sh
```

The CI workflow in `.github/workflows/ci.yml` runs the same gates on pushes and pull requests. CI provisions PostgreSQL so database-backed tests exercise migrations and runtime behavior against the real provider.

## Review Checklist

Before merging risky changes, verify:

- Tenant-scoped queries always filter by tenant.
- Agent endpoints validate `X-Agent-Token`, tenant, and environment.
- Work-item claims enforce owner, status, and lease expiry.
- Retries cannot loop forever and non-retryable failures do not enqueue work.
- Migrations, EF model configuration, and model snapshot are updated together.
- Public endpoints have authentication, signature validation, or explicit unauthenticated design.
- Execution state transitions are covered by tests.
- Docs and backlog reflect the shipped behavior and remaining limitations.

## Known Limits

These controls raise confidence; they do not prove correctness. The next controls to add are end-to-end packaged-agent smoke tests, coverage reporting for critical paths, broader authorization matrix tests, dependency vulnerability scanning, and load/concurrency tests for claims, retries, and scheduling.
