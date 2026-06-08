# Docker Images

This note is for maintainers publishing Serto runtime images.

## Images

Serto publishes two Docker images:

| Image | Purpose |
|-------|---------|
| `craytech/serto.controlplane` | ASP.NET Core control plane plus built React UI |
| `craytech/serto.agent` | Runtime agent that polls the control plane and executes integrations |

Use immutable version tags for deployments and documentation. `latest` can be published as a convenience tag, but production examples should prefer a concrete version such as `1.0.3`.

## Build And Push

Build and publish the control plane from the repository root:

```bash
docker build \
  -f src/ControlPlane/Dockerfile \
  -t craytech/serto.controlplane:1.0.3 \
  .

docker push craytech/serto.controlplane:1.0.3

docker tag craytech/serto.controlplane:1.0.3 craytech/serto.controlplane:latest
docker push craytech/serto.controlplane:latest
```

Build and publish the runtime agent from the repository root:

```bash
docker build \
  -f src/RuntimeAgent/Dockerfile \
  -t craytech/serto.agent:1.0.3 \
  .

docker push craytech/serto.agent:1.0.3

docker tag craytech/serto.agent:1.0.3 craytech/serto.agent:latest
docker push craytech/serto.agent:latest
```

## Deployment Model

The control plane image is usually deployed on an application host and pointed at PostgreSQL running on managed database infrastructure or a separate database host.

The runtime agent image should be deployed wherever it has network access to the systems the integrations need to reach. That can be the same Docker host for a trial, but in production it is often inside the customer's private network while the control plane is hosted elsewhere. Multiple agent hosts can run the same image with different `SERTO_AGENT_TOKEN` and `SERTO_AGENT_ENVIRONMENT` values.

Compose examples:

| File | Purpose |
|------|---------|
| `docker-compose.prod.yml` | Control plane only, pointed at an external PostgreSQL connection string |
| `docker-compose.agent.yml` | Runtime agent only, suitable for each agent host |
| `docker-compose.trial.yml` | Single-host trial with PostgreSQL plus control plane |
