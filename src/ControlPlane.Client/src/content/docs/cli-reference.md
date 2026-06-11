# CLI reference

`serto` is the command-line tool for scaffolding, testing, and deploying integrations. Install it as a .NET global tool, then run it from your project directory.

## Commands

### `serto init <name>`
Scaffold a new integrations project — a .NET project that references `Serto.Sdk`, ready for your first integration.

### `serto scan`
List the integrations the control plane will discover in your project, with their triggers and required capabilities. Runs offline, so it's a fast preflight before you deploy.

### `serto test`
Run an integration locally against test inputs, so you can iterate without deploying.

### `serto dev`
A local development loop for faster iteration while you're writing integrations.

### `serto login` / `serto logout`
Authenticate the CLI against a control plane so `deploy` and other commands can reach it.

### `serto deploy --url <control-plane>`
Package your integrations and upload them to the control plane, which provisions their triggers. The core of the workflow.

### `serto package`
Inspect and manage the integration packages you've deployed.

## A typical loop

```bash
serto init my-integrations
cd my-integrations
# write an integration...
serto scan                               # see what will be provisioned
serto deploy --url http://localhost:8080
```

## Next steps

- [Quick start](/docs/quickstart)
- [Writing integrations](/docs/writing-integrations)
