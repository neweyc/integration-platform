"""The Serto Python harness — the mirror of the agent's SubprocessRunner.

The agent launches this (``python -m serto``) in the package directory, writes one invocation JSON object
to stdin, and reads wire-protocol events (JSON-lines) from stdout. The harness reads the invocation,
resolves the declared entrypoint, runs it with a Context, and emits a terminal ``result`` event.

Contract: docs/multi-language-runtimes.md.
"""

import asyncio
import importlib
import importlib.util
import inspect
import json
import os
import sys
import traceback

from .context import Context


def _resolve_entrypoint(spec):
    """Resolve an entrypoint string into a callable.

    Forms:
      * ``file.py:function`` — load the file (relative to the working directory) and read ``function``.
      * ``module:function``  — import the module and read ``function``.
    """
    if not spec or ":" not in spec:
        raise ValueError(
            "Entrypoint must be 'file.py:function' or 'module:function', got %r" % (spec,))

    module_part, func_name = spec.rsplit(":", 1)

    if module_part.endswith(".py"):
        path = os.path.abspath(module_part)
        if not os.path.exists(path):
            raise FileNotFoundError("Entrypoint file not found: %s" % path)
        mod_name = os.path.splitext(os.path.basename(path))[0]
        loaded = importlib.util.spec_from_file_location(mod_name, path)
        module = importlib.util.module_from_spec(loaded)
        loaded.loader.exec_module(module)
    else:
        module = importlib.import_module(module_part)

    if not hasattr(module, func_name):
        raise AttributeError("Entrypoint '%s' has no attribute '%s'" % (module_part, func_name))
    return getattr(module, func_name)


def run(invocation, emit):
    """Resolve and execute the integration. Raises on failure; the caller maps that to a result event.

    Supports both sync handlers and ``async def`` handlers.
    """
    ctx = Context(invocation, emit)
    handler = _resolve_entrypoint(invocation.get("entrypoint", ""))
    result = handler(ctx)
    if inspect.iscoroutine(result):
        asyncio.run(result)


def main(stdin=None, stdout=None):
    """Entry point for ``python -m serto``. Reads the invocation, runs it, emits a result.

    stdin/stdout are injectable for testing. The integration's own stdout is redirected to stderr so a
    stray ``print()`` can never corrupt the JSON-lines protocol channel.
    """
    protocol_out = stdout if stdout is not None else sys.stdout
    source = stdin if stdin is not None else sys.stdin

    saved_stdout = sys.stdout
    sys.stdout = sys.stderr

    def emit(event):
        protocol_out.write(json.dumps(event) + "\n")
        protocol_out.flush()

    try:
        invocation = json.loads(source.read())
        run(invocation, emit)
        emit({"type": "result", "succeeded": True, "error": None})
    except Exception:
        emit({"type": "result", "succeeded": False, "error": traceback.format_exc()})
    finally:
        sys.stdout = saved_stdout
