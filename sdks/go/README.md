# Serto Go SDK

Write Serto integrations in Go. An integration is a program whose `main()` calls `serto.Run`:

```go
package main

import serto "github.com/neweyc/integration-platform/sdks/go/serto"

func main() {
	serto.Run(func(ctx *serto.Context) error {
		ctx.Logger.Infof("Running in %s", ctx.Execution.Environment)

		token := ctx.Secrets["API_KEY"]      // a provisioned secret
		_ = token

		// ... do the work ...

		return ctx.Publish("orders.synced", map[string]int{"count": 42})
	})
}
```

Returning a non-nil error (or panicking) fails the run; the message is reported.

## How it runs

Go integrations ship as a **container image** (`runtime: "container"`): the image's entrypoint is the
compiled binary, which the agent runs with `docker run -i`, sending the invocation on stdin and reading
structured events (logs, published messages, the result) from stdout. Building inside the image solves
the platform/cross-compile concerns that a shipped binary would have. The protocol is documented in
[`docs/multi-language-runtimes.md`](../../docs/multi-language-runtimes.md).

Scaffold a containerized Go integration with `serto init --runtime go <name>` — it generates `main.go`,
`go.mod`, a `Dockerfile`, and `serto.json`.

## The Context

| member | description |
|---|---|
| `ctx.Logger` | `.Trace/.Debug/.Info/.Warn/.Error` (+ `…f` variants) — captured into execution history |
| `ctx.Secrets` | `map[string]string` of secrets provisioned for the environment |
| `ctx.Payload` / `ctx.PayloadJSON(&v)` | raw / parsed webhook or message body (empty for scheduled) |
| `ctx.Trigger` | how the run was triggered (`ctx.Trigger["type"]` + source-specific fields) |
| `ctx.Execution` | `.ExecutionID`, `.IntegrationName`, `.Environment`, `.ScheduledAt`, … |
| `ctx.Publish(subject, body)` | publish a message; a non-string body is JSON-encoded |

## Tests

```sh
cd sdks/go/serto && go test ./...
```

> Released from this monorepo via a path-prefixed tag (`sdks/go/serto/vX.Y.Z`). To use it from another
> module before a release exists, point it at a local checkout:
> `go mod edit -replace github.com/neweyc/integration-platform/sdks/go/serto=/path/to/serto/sdks/go/serto`
