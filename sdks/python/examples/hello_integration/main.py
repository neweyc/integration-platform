"""A minimal Serto integration in Python.

Run on the platform by deploying this directory (with its serto.json). Locally, the agent launches
``python -m serto`` here and calls ``handler`` with a Context.
"""


def handler(ctx):
    ctx.logger.info("Hello from Python — environment: %s" % ctx.execution.environment)

    # Secrets provisioned for this integration's environment arrive in ctx.secrets.
    if "API_KEY" in ctx.secrets:
        ctx.logger.info("API_KEY is present (%d chars)" % len(ctx.secrets["API_KEY"]))
    else:
        ctx.logger.warning("API_KEY is not configured")

    # Webhook/message payloads arrive as a raw string; payload_json() parses it.
    if ctx.payload:
        ctx.logger.info("Received payload: %s" % ctx.payload)

    # Publish a message other integrations can subscribe to.
    ctx.publish("python.greeted", {"greeted": True, "environment": ctx.execution.environment})
