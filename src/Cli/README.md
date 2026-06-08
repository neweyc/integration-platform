# Serto CLI

The `serto` command-line tool for scaffolding, testing, scanning, packaging, and deploying [Serto](https://github.com/neweyc/integration-platform) integrations.

## Install

```
dotnet tool install -g Serto.Cli
```

## Commands

| Command | Purpose |
|---|---|
| `serto init [name]` | Scaffold a new integration project |
| `serto scan` | Preview integrations, triggers, and required secrets discovered from the current project |
| `serto package` | Build, validate, SHA-256, and archive the project without uploading |
| `serto deploy` | Run the scan preview, then upload and auto-provision in the control plane |
| `serto test` | Run an integration locally |
| `serto dev` | Watch source files and re-run tests on save |
| `serto webhook replay` | Sign and POST a sample webhook payload to a running control plane |

## Quick start

```bash
# Scaffold a new project
serto init MyIntegration

# Run locally during development
cd MyIntegration
serto dev

# Preview what will be deployed
serto scan

# Deploy to the control plane
SERTO_API_TOKEN=pat_... serto deploy --url http://your-control-plane
```

## Related packages

- [`Serto.Sdk`](https://www.nuget.org/packages/Serto.Sdk) — core interfaces and attributes
- [`Serto.Connectors`](https://www.nuget.org/packages/Serto.Connectors) — HTTP and SQL connectors
- [`Serto.Testing`](https://www.nuget.org/packages/Serto.Testing) — test helpers
