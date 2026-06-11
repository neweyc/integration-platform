import io
import json
import os
import sys
import unittest

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from serto._harness import main, run  # noqa: E402

FIXTURES = os.path.join(os.path.dirname(__file__), "fixtures.py")


def invocation(func, payload=None, secrets=None):
    return {
        "protocolVersion": "1",
        "entrypoint": FIXTURES + ":" + func,
        "execution": {"environment": "production"},
        "trigger": {"type": "manual"},
        "payload": payload,
        "secrets": secrets or {},
    }


class RunTests(unittest.TestCase):
    def _events(self, func, **kw):
        events = []
        run(invocation(func, **kw), events.append)
        return events

    def test_logs_and_publishes(self):
        events = self._events("success_handler")
        self.assertIn({"type": "log", "level": "Information", "message": "ran ok"}, events)
        message = next(e for e in events if e["type"] == "message")
        self.assertEqual("test.subject", message["subject"])
        self.assertEqual({"k": 1}, json.loads(message["body"]))

    def test_secrets_are_available(self):
        events = self._events("secret_handler", secrets={"API_KEY": "xyz"})
        self.assertTrue(any("secret=xyz" in e.get("message", "") for e in events))

    def test_failure_propagates(self):
        with self.assertRaises(RuntimeError):
            self._events("failing_handler")

    def test_async_handler_is_awaited(self):
        events = self._events("async_handler")
        self.assertTrue(any(e.get("message") == "async ran" for e in events))


class MainTests(unittest.TestCase):
    def _run_main(self, func, **kw):
        out = io.StringIO()
        main(stdin=io.StringIO(json.dumps(invocation(func, **kw))), stdout=out)
        return [json.loads(line) for line in out.getvalue().splitlines() if line.strip()]

    def test_success_emits_result_true(self):
        events = self._run_main("success_handler")
        self.assertEqual({"type": "result", "succeeded": True, "error": None}, events[-1])

    def test_failure_emits_result_false_with_traceback(self):
        events = self._run_main("failing_handler")
        result = events[-1]
        self.assertEqual("result", result["type"])
        self.assertFalse(result["succeeded"])
        self.assertIn("boom", result["error"])


if __name__ == "__main__":
    unittest.main()
