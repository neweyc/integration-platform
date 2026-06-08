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

Images must be published as **multi-architecture** manifests covering both `linux/amd64` and `linux/arm64`. A plain `docker build` only produces an image for the machine you build on — building on an Apple Silicon Mac yields an arm64-only image, and pulling it on an amd64 Linux host fails with `no matching manifest for linux/amd64`. Use `docker buildx` with explicit `--platform` so a single manifest serves both architectures. The Dockerfiles already cross-compile correctly via `$BUILDPLATFORM`/`TARGETARCH`.

One-time setup of a builder that can produce multi-platform images (the default `docker` driver cannot push them):

```bash
docker buildx create --name serto --driver docker-container --use --bootstrap
```

Build and publish the control plane from the repository root. `buildx` builds and pushes the manifest in a single step — there is no separate `docker push`, because a multi-platform image is not loaded into the local Docker engine:

```bash
docker buildx build \
  --platform linux/amd64,linux/arm64 \
  -f src/ControlPlane/Dockerfile \
  -t craytech/serto.controlplane:1.0.3 \
  -t craytech/serto.controlplane:latest \
  --push \
  .
```

Build and publish the runtime agent from the repository root:

```bash
docker buildx build \
  --platform linux/amd64,linux/arm64 \
  -f src/RuntimeAgent/Dockerfile \
  -t craytech/serto.agent:1.0.3 \
  -t craytech/serto.agent:latest \
  --push \
  .
```

After pushing, confirm both architectures are present in the manifest:

```bash
docker buildx imagetools inspect craytech/serto.agent:1.0.3
```

The output should list a platform entry for both `linux/amd64` and `linux/arm64`.

## CLI Tool Package

The CLI is packaged as a .NET global tool with package id `Serto.Cli` and command name `serto`.

Build and publish the tool package:

```bash
dotnet pack src/Cli/Cli.csproj -c Release -o artifacts/packages -p:NuGetAudit=false
dotnet nuget push artifacts/packages/Serto.Cli.1.0.3.nupkg \
  --api-key "$NUGET_API_KEY" \
  --source https://api.nuget.org/v3/index.json
```

Install it on a fresh machine:

```bash
dotnet tool install --global Serto.Cli --version 1.0.3
serto --help
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
