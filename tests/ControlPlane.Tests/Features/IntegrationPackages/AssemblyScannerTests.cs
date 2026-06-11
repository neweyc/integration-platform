using ControlPlane.Features.IntegrationPackages.Scanning;
using Serto.Sdk;
using Shared.Domain;

namespace ControlPlane.Tests.Features.IntegrationPackages;

public class AssemblyScannerTests
{
    [Fact]
    public void ScanAssembly_BaseIntegrationAttribute_DiscoversIntegrationWithoutTriggers()
    {
        var discovered = AssemblyScanner.ScanAssembly(typeof(BaseOnlyDiscoveredIntegration).Assembly);

        var integration = discovered.Single(i => i.Slug == "scanner-base-only");

        Assert.Equal("Scanner Base Only", integration.Name);
        Assert.Equal(typeof(BaseOnlyDiscoveredIntegration).FullName, integration.ClassName);
        Assert.Equal("Base metadata only", integration.Description);
        Assert.Equal(45, integration.TimeoutSeconds);
        Assert.Empty(integration.Triggers);
    }

    [Fact]
    public void ScanAssembly_ScheduledAndWebhookAttributes_DiscoversOneIntegrationWithTriggerRecords()
    {
        var discovered = AssemblyScanner.ScanAssembly(typeof(MultiTriggerDiscoveredIntegration).Assembly);

        var integration = discovered.Single(i => i.Slug == "scanner-multi-trigger");

        Assert.Equal("Scanner Multi Trigger", integration.Name);
        Assert.Equal(typeof(MultiTriggerDiscoveredIntegration).FullName, integration.ClassName);
        Assert.Equal(300, integration.TimeoutSeconds);
        Assert.Equal(2, integration.RetryMaxAttempts);
        Assert.Equal(60, integration.RetryBackoffSeconds);
        Assert.Contains(integration.Triggers, t =>
            t.Name == "Scheduled"
            && t.Slug == "scheduled"
            && t.Type == TriggerType.Scheduled
            && t.CronExpression == "0 * * * *");
        Assert.Contains(integration.Triggers, t =>
            t.Name == "Webhook"
            && t.Slug == "webhook"
            && t.Type == TriggerType.Webhook
            && t.CronExpression is null);
    }

    [Fact]
    public void ScanAssembly_MessageIntegrationAttribute_DiscoversQueueTriggerWithSubject()
    {
        var discovered = AssemblyScanner.ScanAssembly(typeof(MessageDiscoveredIntegration).Assembly);

        var integration = discovered.Single(i => i.Slug == "scanner-message");
        var trigger = Assert.Single(integration.Triggers);

        Assert.Equal("Message", trigger.Name);
        Assert.Equal("message", trigger.Slug);
        Assert.Equal(TriggerType.Queue, trigger.Type);
        Assert.Equal("high-wind", trigger.Subject);
        Assert.Null(trigger.CronExpression);
    }

    [Fact]
    public void ScanAssembly_RequiresAgentCapabilities_DiscoversNormalizedRequiredTags()
    {
        var discovered = AssemblyScanner.ScanAssembly(typeof(CapabilityTaggedIntegration).Assembly);

        var integration = discovered.Single(i => i.Slug == "scanner-capability-tagged");

        // Tags are trimmed and de-duplicated (case-insensitive).
        Assert.NotNull(integration.RequiredTags);
        Assert.Equal(2, integration.RequiredTags!.Count);
        Assert.Contains("hardware-signal", integration.RequiredTags);
        Assert.Contains("site-floor-1", integration.RequiredTags);
    }

    [Fact]
    public void ScanAssembly_NoCapabilityAttribute_HasNoRequiredTags()
    {
        var discovered = AssemblyScanner.ScanAssembly(typeof(BaseOnlyDiscoveredIntegration).Assembly);

        var integration = discovered.Single(i => i.Slug == "scanner-base-only");

        Assert.True(integration.RequiredTags is null || integration.RequiredTags.Count == 0);
    }

    [Integration("Scanner Base Only", "scanner-base-only", Description = "Base metadata only", TimeoutSeconds = 45)]
    private sealed class BaseOnlyDiscoveredIntegration : IIntegration
    {
        public Task RunAsync(IIntegrationContext context, CancellationToken ct) => Task.CompletedTask;
    }

    [Integration("Scanner Capability Tagged", "scanner-capability-tagged")]
    [RequiresAgentCapabilities("hardware-signal", " site-floor-1 ", "Hardware-Signal")]
    private sealed class CapabilityTaggedIntegration : IIntegration
    {
        public Task RunAsync(IIntegrationContext context, CancellationToken ct) => Task.CompletedTask;
    }

    [MessageIntegration("Scanner Message", "scanner-message", subject: "high-wind")]
    private sealed class MessageDiscoveredIntegration : IIntegration
    {
        public Task RunAsync(IIntegrationContext context, CancellationToken ct) => Task.CompletedTask;
    }

    [ScheduledIntegration(
        "Scanner Multi Trigger",
        "scanner-multi-trigger",
        "0 * * * *",
        Description = "Runs hourly or through a webhook.",
        TimeoutSeconds = 300,
        RetryMaxAttempts = 2,
        RetryBackoffSeconds = 60)]
    [WebhookIntegration(
        "Scanner Multi Trigger",
        "scanner-multi-trigger",
        Description = "Runs hourly or through a webhook.",
        TimeoutSeconds = 300,
        RetryMaxAttempts = 2,
        RetryBackoffSeconds = 60)]
    private sealed class MultiTriggerDiscoveredIntegration : IIntegration
    {
        public Task RunAsync(IIntegrationContext context, CancellationToken ct) => Task.CompletedTask;
    }
}
