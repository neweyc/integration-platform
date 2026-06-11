"""Handlers referenced by test_harness via file-path entrypoints."""


def success_handler(ctx):
    ctx.logger.info("ran ok")
    ctx.publish("test.subject", {"k": 1})


def secret_handler(ctx):
    ctx.logger.info("secret=" + ctx.secrets.get("API_KEY", ""))


def failing_handler(ctx):
    raise RuntimeError("boom")


async def async_handler(ctx):
    ctx.logger.info("async ran")
