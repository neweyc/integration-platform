# Multi-language runtimes

> Status: in progress. This document is the **contract** that the control plane, the runtime agent, the
> CLI, and every language SDK implement. Pin changes here first, then change code to match.

Serto integrations have always been .NET classes loaded into the agent and invoked through
`IIntegration.RunAsync`. To run integrations written in other languages (Python, Node.js, Go, …) the
platform keeps that orchestration substrate — scheduling, leasing, secrets, logs, triggers, retries —
exactly as is, and changes only the **execution edge**: how the agent hands work to an integration and
gets results back.

Two artifacts define that edge:

1. **The package manifest (`serto.json`)** — a declarative description of what's in a package. It
   replaces server-side reflection for non-.NET runtimes (which can't be reflected) and is the single
   source of truth for the integrations, triggers, and required secrets a package provides.
2. **The wire protocol** — how the agent passes an invocation to an out-of-process integration and how
   that process streams logs, published messages, and its result back.

The in-process .NET path is unchanged and remains the default fast path. Everything below concerns
out-of-process runtimes (and, optionally, .NET running out-of-process for uniform isolation).

---

## 1. The package manifest — `serto.json`

A package is an archive (zip/tar) with a `serto.json` at its root. For .NET packages the CLI can still
generate this manifest by reflecting over the build output, so authors don't hand-write it; for other
languages the SDK/CLI emits it from decorators or an explicit declaration.

```jsonc
{
  "manifestVersion": "1",
  "runtime": "python",                       // dotnet | python | node | go | container
  "integrations": [
    {
      "name": "Sync Orders",                 // human-readable
      "slug": "sync-orders",                  // stable id within the tenant/environment
      "entrypoint": "main.py:handler",        // runtime-specific (see below)
      "description": "Sync orders from A to B",
      "timeoutSeconds": 300,                   // optional; null = platform default
      "retry": { "maxAttempts": 3, "backoffSeconds": 30 },   // optional
      "requiredSecrets": ["API_KEY", "DB_CONNECTION"],        // names only, never values
      "requiredCapabilities": ["site-floor-1"],               // agent tags this needs
      "triggers": [
        { "type": "scheduled", "cron": "0 * * * *" },
        { "type": "webhook" },
        { "type": "message", "subject": "orders.created" }
      ]
    }
  ]
}
```

### `runtime`

Selects the agent runner. `dotnet` is the in-process default; everything else runs out-of-process. The
control plane stores this per package and stamps it onto every dispatched work item so the agent can
pick the right runner without inspecting the artifact.

### `entrypoint` (runtime-specific)

The agent's runner is the only component that interprets this string:

| runtime   | entrypoint form            | meaning                                          |
|-----------|----------------------------|--------------------------------------------------|
| dotnet    | `MyCo.Integrations.Sync`   | fully-qualified class implementing `IIntegration` (back-compat with today's `ClassName`) |
| python    | `main.py:handler`          | module/file `:` callable                         |
| node      | `index.js#handler`         | file `#` named export                            |
| go        | `./sync`                   | path to a built binary inside the package        |
| container | *(optional)* `cmd arg…`    | override CMD; empty uses the image's entrypoint  |
| shell     | `./close.sh`, `sqlplus … @x.sql` | a raw command line run through the host shell — **no SDK** (see §4) |

`entrypoint` generalizes today's `Integration.ClassName`. The control plane stores it in the same field
(renamed/loosened — see the control-plane task) and the regex that currently validates it as a CLR type
name is relaxed.

### Triggers, secrets, capabilities

These mirror the existing domain exactly — the manifest is just a language-neutral way to declare them
instead of C# attributes. `requiredSecrets` lists names the platform must provision; values are never in
the manifest. `requiredCapabilities` maps to the existing agent-tag routing
([agent-capability-tags.md](./agent-capability-tags.md)).

---

## 2. The wire protocol

When an out-of-process integration is due, the agent launches the integration as a child process and
speaks a small, versioned protocol with it. The protocol is deliberately transport-simple — stdin in,
stdout out — so a thin SDK in any language can implement it in well under a hundred lines.

### Invocation (agent → integration)

The agent writes **one JSON object** to the process's **stdin**, then closes stdin (EOF). Secrets travel
inside this payload rather than the environment, so they never appear in the process table or leak to
grandchild processes' `environ`.

```jsonc
{
  "protocolVersion": "1",
  "entrypoint": "main.py:handler",
  "execution": {
    "executionId": "0c5e…",
    "integrationId": "9a1f…",
    "integrationName": "Sync Orders",
    "environment": "production",
    "scheduledAt": "2026-06-11T08:00:00Z"
  },
  "trigger": { "type": "scheduled", "cron": "0 * * * *" },   // shape mirrors TriggerInfo
  "payload": "{\"event\":\"created\"}",                       // raw string, or null
  "secrets": { "API_KEY": "…", "DB_CONNECTION": "…" }
}
```

A few bootstrap values are also passed as environment variables so an SDK can detect it's running under
Serto before reading stdin: `SERTO_PROTOCOL_VERSION`, `SERTO_EXECUTION_ID`.

### Events (integration → agent)

The integration writes **newline-delimited JSON** ("JSON lines") to **stdout**. Each line is one event.
The agent translates these onto the same `RunRequest` sink the in-process path uses
(`IntegrationLogger`, `Publisher`), so logs and messages from a Python integration are indistinguishable
from a C# one downstream.

```jsonc
{"type":"log","level":"Information","message":"Fetched 42 orders","timestamp":"2026-06-11T08:00:01Z","exception":null,"properties":{}}
{"type":"message","subject":"orders.created","body":"{\"id\":123}"}
{"type":"result","succeeded":true,"error":null}
```

| event     | agent action                                                                 |
|-----------|------------------------------------------------------------------------------|
| `log`     | forward to the execution logger (same path as `context.Logger`)              |
| `message` | publish via the message publisher (same path as `context.Messages`)          |
| `result`  | terminal outcome for the run; `succeeded:false` + `error` reports a business failure |

`level` uses the same names as .NET `LogLevel` (`Trace`/`Debug`/`Information`/`Warning`/`Error`/`Critical`).

### Outcome mapping

The agent decides the run's outcome from the result event and the process exit, so an SDK can be minimal:

| condition                                   | outcome   |
|---------------------------------------------|-----------|
| exit 0, a `result` with `succeeded:true` (or no result line) | **success** |
| a `result` with `succeeded:false`           | **failure** (uses `error`)                |
| non-zero exit, no `result`                  | **failure** (error = captured stderr tail) |
| agent kills the process at the timeout      | **timeout**                                |
| agent shutdown / cancellation               | **cancelled** (non-retryable)              |

**stderr** is captured for diagnostics and folded into the error message on a non-zero exit. This keeps
the contract forgiving: a script that just prints to stderr and exits non-zero still fails cleanly with a
useful message, even without emitting a single protocol event.

These outcomes are exactly the ones `IntegrationExecutor` already maps for the in-process runner, so the
subprocess runner reuses the same completion/timeout/cancellation lifecycle — it only changes *how the
run is performed and observed*, never how results are reported to the control plane.

---

## 3. Isolation

Out-of-process execution also closes a gap that exists today: in-process .NET integrations share the
agent's process and can crash or starve it. Two out-of-process strategies:

- **Subprocess** (`SubprocessRunner`) — the agent launches the language runtime directly (`python -m serto`,
  a compiled binary, …). Simplest; requires the runtime installed on the agent host; isolation is
  process-level only. Good for dev and self-hosted v1. Configured per runtime via `Agent:Runtimes`.
- **Container** (`ContainerRunner`) — the agent runs an OCI image: `docker run --rm -i <image>`, speaking
  the identical wire protocol over the container's stdin/stdout. Strong isolation and heterogeneous
  runtimes with no host prerequisites beyond a container engine. The production isolation story and the
  natural fit for the hosted-SaaS phase.

  For a container integration, `runtime` is `"container"` and the integration's **entrypoint is the image
  reference** (e.g. `ghcr.io/acme/sync:1.0`, digest pins allowed). The image is self-contained — it carries
  the integration and its language harness — so build and platform concerns live inside the image rather
  than on the agent. That is what makes containers the clean host for **compiled runtimes (Go, Rust, …)**:
  build in the image, run in the image. Secrets ride in the stdin invocation, not env vars, so nothing
  sensitive appears in `docker inspect`. The engine and base run args are configurable via
  `Agent:Container` (default `docker run --rm -i`); a unique `--name` per run lets the agent stop exactly
  that container on timeout/shutdown.

All three speak the identical wire protocol; only the launch mechanism differs, so they are runners behind
the same seam (`WireProtocolHost` holds the shared stdin/stdout/outcome handling).

---

## 4. Raw scripts — the shell runtime

Not every job is worth wrapping in an SDK. The **`shell`** runtime (`ShellRunner`) runs a raw command or
script — a `.sh`, a `sqlplus … @script.sql`, any executable — with **no SDK and no wire protocol**. It's
the bring-your-existing-scripts path: get scheduling, secrets, logs, retries, and alerts around the jobs
you already run under cron / Control-M / EBS, without rewriting them.

The contract is the one every script runner already uses:

- **Declared** by a manifest `runtime: "shell"` with an `entrypoint` that is a command line (`./close.sh`,
  `sqlplus -s "$DB_USER/$DB_PW@orcl" @close.sql`). It runs through the agent's configured shell
  (`Agent:Shell`, default `/bin/sh -c`), with the package directory as the working directory.
- **Inputs as environment variables** — secrets under their own names (a secret `DB_PW` is `$DB_PW`), plus
  `SERTO_EXECUTION_ID`, `SERTO_INTEGRATION_NAME`, `SERTO_ENVIRONMENT`, `SERTO_SCHEDULED_AT`,
  `SERTO_TRIGGER_TYPE`, and (for webhook/message triggers) `SERTO_PAYLOAD` / `SERTO_MESSAGE_SUBJECT`.
- **All stdout and stderr is captured as logs** (stderr at Warning, for visibility — it does not by itself
  mean failure).
- **The exit code is the outcome:** `0` = success; non-zero = failure with the stderr tail as the reason. A
  timeout kills the process tree and reports a timeout, exactly like the other runners.

Trade-offs vs the SDK path: secrets arrive as env vars (visible to the process, as every scheduler does)
rather than over stdin, and the agent host must have whatever the script needs (a shell, `sqlplus`, …) — or
run the script inside a container image. Because a shell integration runs arbitrary code on the agent host,
treat deploy rights to it as you would shell access (see the Authz Revisit backlog item).

---

## 5. What does *not* change

The orchestration substrate is already language-neutral and is untouched by all of the above:
scheduling and cron evaluation, work-item claiming and leasing, the secret manifest, log ingestion,
execution records, triggers, messages, workflows, and retries. Only package discovery (manifest instead
of reflection) and the execution edge (wire protocol instead of an in-process call) change.

---

## 6. Authoring with the CLI

The `serto` CLI treats a directory as a **manifest project** when it contains a `serto.json` whose
runtime is not `dotnet`; otherwise it uses the existing `.csproj` flow. The same commands work for both:

```sh
serto init my-integration --runtime python   # scaffold main.py + serto.json
serto scan                                    # preview integrations/triggers from the manifest
serto package                                 # zip the source (no build) — serto.json at the root
serto deploy                                  # package + upload; the control plane reads the manifest
```

For a non-.NET project the CLI does **not** build or reflect: `scan`/`deploy` read `serto.json`
directly, and `package` zips the project source (excluding `.git`, `bin`, `obj`, `node_modules`, and
language caches). The control plane then discovers integrations from the manifest on upload.

`serto init` scaffolds `dotnet`, `python`, `node`, `go`, and `shell`:

- `--runtime python` → `main.py` + `serto.json` (runtime `python`, subprocess).
- `--runtime node` → `index.js` + `package.json` + `serto.json` (runtime `node`, subprocess).
- `--runtime shell` → `job.sh` + `serto.json` (runtime `shell`) — a raw script with scheduling, secrets,
  logs, and retries around it; no SDK (see §4).
- `--runtime go` → `main.go` + `go.mod` + `Dockerfile` + `serto.json` (runtime `container`). Go is
  compiled, so it ships as an image: build & push it, set the image as the integration's `entrypoint`,
  then `serto deploy`. The agent's `ContainerRunner` runs it.

SDKs live under `sdks/` (Python, Node, Go). Subprocess runtimes (python, node) ship source only and run a
dependency-free integration directly; integrations with third-party dependencies should use a container
image. Other runtimes deploy fine once their `serto.json` is authored by hand.
