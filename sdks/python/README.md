# Serto Python SDK

```sh
pip install serto-sdk    # distribution name; the import package is `serto`
```

Write Serto integrations in Python. An integration is any callable taking a `Context`:

```python
# main.py
def handler(ctx):
    ctx.logger.info(f"Running in {ctx.execution.environment}")

    order = ctx.payload_json()              # webhook/message body, parsed
    api_key = ctx.secrets["API_KEY"]        # provisioned secret

    # ... do the work ...

    ctx.publish("orders.synced", {"count": 42})   # message other integrations can subscribe to
```

Declare it in a `serto.json` next to your code:

```json
{
  "manifestVersion": "1",
  "runtime": "python",
  "integrations": [
    {
      "name": "Sync Orders",
      "slug": "sync-orders",
      "entrypoint": "main.py:handler",
      "triggers": [{ "type": "scheduled", "cron": "0 * * * *" }],
      "requiredSecrets": ["API_KEY"]
    }
  ]
}
```

## How it runs

The Serto agent launches `python -m serto` in your integration's directory, sends the invocation on
stdin, and reads structured events (logs, published messages, the result) from stdout. The protocol is
documented in [`docs/multi-language-runtimes.md`](../../docs/multi-language-runtimes.md). `async def`
handlers are supported.

## The Context

| member | description |
|---|---|
| `ctx.logger` | `.trace/.debug/.info/.warning/.error/.critical` — captured into execution history |
| `ctx.secrets` | dict of secrets provisioned for the environment |
| `ctx.payload` / `ctx.payload_json()` | raw / parsed webhook or message body (`None` for scheduled) |
| `ctx.trigger` | how the run was triggered (`ctx.trigger["type"]` + source-specific fields) |
| `ctx.execution` | `.execution_id`, `.integration_name`, `.environment`, `.scheduled_at`, … |
| `ctx.publish(subject, body)` | publish a message; a non-string body is JSON-encoded |

## Tests

```sh
cd sdks/python && python -m unittest discover -s tests
```
