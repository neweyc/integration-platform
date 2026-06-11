"""The execution context handed to a Python integration.

Mirrors the .NET IIntegrationContext: secrets, a structured logger, the trigger, the payload, and a way
to publish messages. Logs and messages are emitted as wire-protocol events (see _harness); the agent
forwards them onto the same pipeline a C# integration uses, so a Python run is indistinguishable
downstream. See docs/multi-language-runtimes.md.
"""

import json


class Execution:
    """Metadata about the current run."""

    def __init__(self, data):
        self.execution_id = data.get("executionId")
        self.integration_id = data.get("integrationId")
        self.integration_name = data.get("integrationName")
        self.environment = data.get("environment")
        self.scheduled_at = data.get("scheduledAt")


class Logger:
    """Structured logger. Levels match .NET LogLevel names so they render identically in execution history."""

    def __init__(self, emit):
        self._emit = emit

    def _log(self, level, message, exception=None):
        event = {"type": "log", "level": level, "message": str(message)}
        if exception is not None:
            event["exception"] = str(exception)
        self._emit(event)

    def trace(self, message):
        self._log("Trace", message)

    def debug(self, message):
        self._log("Debug", message)

    def info(self, message):
        self._log("Information", message)

    def warning(self, message):
        self._log("Warning", message)

    def error(self, message, exception=None):
        self._log("Error", message, exception)

    def critical(self, message, exception=None):
        self._log("Critical", message, exception)


class Context:
    def __init__(self, invocation, emit):
        self._emit = emit
        self.execution = Execution(invocation.get("execution", {}))
        # The trigger is passed through verbatim; read trigger["type"] and source-specific fields.
        self.trigger = invocation.get("trigger", {})
        # Raw request/message body for webhook- and message-triggered runs; None otherwise.
        self.payload = invocation.get("payload")
        self.secrets = invocation.get("secrets", {})
        self.logger = Logger(emit)

    def payload_json(self):
        """Deserialize the payload as JSON, or None when there is no payload."""
        return json.loads(self.payload) if self.payload else None

    def publish(self, subject, body):
        """Publish a message other integrations can subscribe to. A non-string body is JSON-encoded."""
        if not isinstance(body, str):
            body = json.dumps(body)
        self._emit({"type": "message", "subject": subject, "body": body})
