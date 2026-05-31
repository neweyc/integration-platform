using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RuntimeAgent.Agent;
using RuntimeAgent.Execution;

namespace RuntimeAgent.Tests;

public class WorkerTests
{
    [Fact]
    public async Task Worker_PollsControlPlane_OnStart()
    {
        // Arrange
        var controlPlane = Substitute.For<IControlPlaneClient>();
        var loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient("integration").Returns(new HttpClient());

        var options = new AgentOptions
        {
            Environment = "production",
            PollIntervalSeconds = 1,
            MaxConcurrentExecutions = 2,
            IntegrationsPath = "."
        };

        var executor = new IntegrationExecutor(
            controlPlane, loader, httpFactory, options,
            NullLogger<IntegrationExecutor>.Instance);

        controlPlane.GetIntegrationsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<IntegrationItem>());
        controlPlane.GetSecretsAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string>());

        var worker = new Worker(controlPlane, executor, loader, options,
            NullLogger<Worker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act
        try
        {
            await worker.StartAsync(cts.Token);
            await Task.Delay(50, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        // Assert - should have polled at least once
        await controlPlane.Received().GetIntegrationsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Worker_SkipsManualTrigger_WhenPolling()
    {
        // Arrange
        var controlPlane = Substitute.For<IControlPlaneClient>();
        var loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient("integration").Returns(new HttpClient());

        var options = new AgentOptions
        {
            Environment = "production",
            PollIntervalSeconds = 1,
            MaxConcurrentExecutions = 2,
            IntegrationsPath = "."
        };

        var executor = new IntegrationExecutor(
            controlPlane, loader, httpFactory, options,
            NullLogger<IntegrationExecutor>.Instance);

        var integrationId = Guid.NewGuid();

        var integration = new IntegrationItem(
            integrationId,
            "Manual Integration",
            "manual",
            "Manual", // Manual trigger - should never be "due"
            null,
            "Some.ClassName");

        controlPlane.GetIntegrationsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<IntegrationItem> { integration });
        controlPlane.GetSecretsAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string>());

        var worker = new Worker(controlPlane, executor, loader, options,
            NullLogger<Worker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        // Act
        try
        {
            await worker.StartAsync(cts.Token);
            await Task.Delay(100, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        // Assert - should NOT have started any execution (Manual triggers don't fire from polling)
        await controlPlane.DidNotReceive().StartExecutionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Worker_SkipsWebhookTrigger_WhenPolling()
    {
        // Arrange
        var controlPlane = Substitute.For<IControlPlaneClient>();
        var loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient("integration").Returns(new HttpClient());

        var options = new AgentOptions
        {
            Environment = "production",
            PollIntervalSeconds = 1,
            MaxConcurrentExecutions = 2,
            IntegrationsPath = "."
        };

        var executor = new IntegrationExecutor(
            controlPlane, loader, httpFactory, options,
            NullLogger<IntegrationExecutor>.Instance);

        var integrationId = Guid.NewGuid();

        var integration = new IntegrationItem(
            integrationId,
            "Webhook Integration",
            "webhook",
            "Webhook", // Webhook trigger - should never be "due" from polling
            null,
            "Some.ClassName");

        controlPlane.GetIntegrationsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<IntegrationItem> { integration });
        controlPlane.GetSecretsAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string>());

        var worker = new Worker(controlPlane, executor, loader, options,
            NullLogger<Worker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        // Act
        try
        {
            await worker.StartAsync(cts.Token);
            await Task.Delay(100, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        // Assert - should NOT have started any execution
        await controlPlane.DidNotReceive().StartExecutionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Worker_SkipsScheduledWithNoCron_WhenPolling()
    {
        // Arrange
        var controlPlane = Substitute.For<IControlPlaneClient>();
        var loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient("integration").Returns(new HttpClient());

        var options = new AgentOptions
        {
            Environment = "production",
            PollIntervalSeconds = 1,
            MaxConcurrentExecutions = 2,
            IntegrationsPath = "."
        };

        var executor = new IntegrationExecutor(
            controlPlane, loader, httpFactory, options,
            NullLogger<IntegrationExecutor>.Instance);

        var integrationId = Guid.NewGuid();

        // Scheduled but with null cron expression - should never be due
        var integration = new IntegrationItem(
            integrationId,
            "Bad Scheduled Integration",
            "bad-scheduled",
            "Scheduled",
            null, // No cron expression
            "Some.ClassName");

        controlPlane.GetIntegrationsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<IntegrationItem> { integration });
        controlPlane.GetSecretsAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string>());

        var worker = new Worker(controlPlane, executor, loader, options,
            NullLogger<Worker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        // Act
        try
        {
            await worker.StartAsync(cts.Token);
            await Task.Delay(100, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        // Assert - should NOT have started any execution
        await controlPlane.DidNotReceive().StartExecutionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Worker_HandlesControlPlaneError_Gracefully()
    {
        // Arrange
        var controlPlane = Substitute.For<IControlPlaneClient>();
        var loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient("integration").Returns(new HttpClient());

        var options = new AgentOptions
        {
            Environment = "production",
            PollIntervalSeconds = 1,
            MaxConcurrentExecutions = 2,
            IntegrationsPath = "."
        };

        var executor = new IntegrationExecutor(
            controlPlane, loader, httpFactory, options,
            NullLogger<IntegrationExecutor>.Instance);

        // Simulate control plane being unavailable
        controlPlane.GetIntegrationsAsync(Arg.Any<CancellationToken>())
            .Returns<List<IntegrationItem>>(x => throw new HttpRequestException("Control plane unavailable"));

        var worker = new Worker(controlPlane, executor, loader, options,
            NullLogger<Worker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        // Act - should not throw, worker should handle the error and continue
        try
        {
            await worker.StartAsync(cts.Token);
            await Task.Delay(100, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        // Assert - should have attempted to poll (error is handled gracefully)
        await controlPlane.Received().GetIntegrationsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AgentOptions_DefaultValues_AreReasonable()
    {
        var options = new AgentOptions();

        Assert.Equal(30, options.PollIntervalSeconds);
        Assert.Equal(5, options.MaxConcurrentExecutions);
        Assert.Equal("", options.ControlPlaneUrl);
        Assert.Equal("", options.AgentToken);
        Assert.Equal("", options.Environment);
        Assert.Equal("", options.IntegrationsPath);
    }
}
