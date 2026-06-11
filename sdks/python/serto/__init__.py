"""Serto Python SDK — write integrations in Python that run on the Serto platform.

A handler is any callable taking a single ``Context`` argument::

    def handler(ctx):
        ctx.logger.info("running")
        data = ctx.payload_json()
        ctx.publish("orders.synced", {"count": 42})

Declare it in ``serto.json`` with an entrypoint of ``your_file.py:handler``. See
docs/multi-language-runtimes.md.
"""

from .context import Context, Execution, Logger

__all__ = ["Context", "Execution", "Logger"]
__version__ = "0.1.0"
