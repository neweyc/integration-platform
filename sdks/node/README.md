# Serto Node.js SDK

Write Serto integrations in JavaScript. An integration exports a handler taking a `Context`:

```js
// index.js
module.exports.handler = async (ctx) => {
  ctx.logger.info(`Running in ${ctx.execution.environment}`);

  const order = ctx.payloadJson();        // webhook/message body, parsed
  const apiKey = ctx.secrets.API_KEY;     // provisioned secret

  // ... do the work ...

  await ctx.publish('orders.synced', { count: 42 });
};
```

Declare it in `serto.json` (`"entrypoint": "index.js#handler"`, `"runtime": "node"`).

## How it runs

The Serto agent launches the harness (`serto-runtime`) in your integration's directory, sends the
invocation on stdin, and reads structured events (logs, published messages, the result) from stdout.
`async` handlers are awaited. The protocol is documented in
[`docs/multi-language-runtimes.md`](../../docs/multi-language-runtimes.md).

Scaffold one with `serto init --runtime node <name>`. Dependency-free integrations run as a subprocess;
if you need npm dependencies at runtime, ship a container image (`runtime: "container"`) instead.

## The Context

| member | description |
|---|---|
| `ctx.logger` | `.trace/.debug/.info/.warn/.error` — captured into execution history |
| `ctx.secrets` | object of secrets provisioned for the environment |
| `ctx.payload` / `ctx.payloadJson()` | raw / parsed webhook or message body (`null` for scheduled) |
| `ctx.trigger` | how the run was triggered (`ctx.trigger.type` + source-specific fields) |
| `ctx.execution` | `.executionId`, `.integrationName`, `.environment`, `.scheduledAt`, … |
| `ctx.publish(subject, body)` | publish a message; a non-string body is JSON-encoded |

## Tests

```sh
cd sdks/node/serto && node --test
```

Published to npm as the scoped package **`@craytech/serto`** (`npm install @craytech/serto`). Bare `serto`
is rejected by npm's typosquatting guard for being too similar to `serve`, hence the scope.
