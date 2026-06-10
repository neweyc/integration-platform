using Cli.Commands;
using Serto.Sdk;

namespace Cli.Tests;

public class TestCommandPreflightTests
{
    [ScheduledIntegration("Good", "good", "0 * * * *")]
    private sealed class ValidScheduled : IIntegration
    {
        public Task RunAsync(IIntegrationContext context, CancellationToken ct) => Task.CompletedTask;
    }

    [ScheduledIntegration("Bad", "bad", "* * * * * *")] // 6 fields — invalid for standard cron
    private sealed class BadCron : IIntegration
    {
        public Task RunAsync(IIntegrationContext context, CancellationToken ct) => Task.CompletedTask;
    }

    [WebhookIntegration("Hook", "hook")]
    private sealed class WebhookOk : IIntegration
    {
        public Task RunAsync(IIntegrationContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoAttribute : IIntegration
    {
        public Task RunAsync(IIntegrationContext context, CancellationToken ct) => Task.CompletedTask;
    }

    [Integration("Ctor", "ctor")]
    private sealed class NeedsConstructorArgs : IIntegration
    {
        public NeedsConstructorArgs(string required) => _ = required;
        public Task RunAsync(IIntegrationContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private static readonly string[] None = [];

    [Fact]
    public void ValidScheduledIntegration_HasNoErrors()
    {
        var result = TestCommand.Preflight(typeof(ValidScheduled), None, None);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void InvalidCron_IsAnError()
    {
        var result = TestCommand.Preflight(typeof(BadCron), None, None);

        Assert.Contains(result.Errors, e => e.Contains("Cron", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingAttribute_IsAnError()
    {
        var result = TestCommand.Preflight(typeof(NoAttribute), None, None);

        Assert.Contains(result.Errors, e => e.Contains("attribute", StringComparison.Ordinal));
    }

    [Fact]
    public void NonParameterlessConstructor_IsAnError()
    {
        var result = TestCommand.Preflight(typeof(NeedsConstructorArgs), None, None);

        Assert.Contains(result.Errors, e => e.Contains("parameterless constructor", StringComparison.Ordinal));
    }

    [Fact]
    public void WebhookIntegration_NeedsNoCron()
    {
        var result = TestCommand.Preflight(typeof(WebhookOk), None, None);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MissingRequiredSecret_IsAWarningNotAnError()
    {
        var result = TestCommand.Preflight(typeof(ValidScheduled), ["SHOPIFY_API_KEY"], None);

        Assert.False(result.HasErrors);
        Assert.Contains(result.Warnings, w => w.Contains("SHOPIFY_API_KEY", StringComparison.Ordinal));
    }

    [Fact]
    public void ProvidedRequiredSecret_ProducesNoWarning_CaseInsensitive()
    {
        var result = TestCommand.Preflight(typeof(ValidScheduled), ["API_KEY"], ["api_key"]);

        Assert.Empty(result.Warnings);
    }
}
