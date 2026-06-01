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
    public async Task Worker_DispatchesClaimedIntegrations_FromPoll()
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
            IntegrationsPath = AppContext.BaseDirectory // Point to test assembly directory
        };

        var executor = new IntegrationExecutor(
            controlPlane, loader, httpFactory, options,
            NullLogger<IntegrationExecutor>.Instance);

        var integrationId = Guid.NewGuid();
        var leaseExpires = DateTime.UtcNow.AddMinutes(5);

        // The control plane returns only integrations that are due AND claimed
        // Use a real integration class that exists in the test assembly
        var integration = new IntegrationItem(
            integrationId,
            "Sync Orders",
            "sync-orders",
            "Scheduled",
            "0 * * * *",
            typeof(SuccessfulTestIntegration).FullName!,
            leaseExpires);

        controlPlane.GetIntegrationsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<IntegrationItem> { integration });
        controlPlane.GetSecretsAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string>());
        controlPlane.StartExecutionAsync(integrationId, Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        var worker = new Worker(controlPlane, executor, loader, options,
            NullLogger<Worker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        try
        {
            await worker.StartAsync(cts.Token);
            await Task.Delay(150, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        // Assert - should have attempted to start execution for the claimed integration
        await controlPlane.Received().StartExecutionAsync(integrationId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Worker_FetchesSecrets_WhenIntegrationsAreClaimed()
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

        var integration = new IntegrationItem(
            Guid.NewGuid(),
            "Test",
            "test",
            "Scheduled",
            "0 * * * *",
            "Some.ClassName",
            DateTime.UtcNow.AddMinutes(5));

        controlPlane.GetIntegrationsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<IntegrationItem> { integration });
        controlPlane.GetSecretsAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string> { ["API_KEY"] = "secret" });

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

        // Assert - should have fetched secrets when integrations are due
        await controlPlane.Received().GetSecretsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Worker_SkipsSecretsCall_WhenNoIntegrationsClaimed()
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

        // No integrations claimed
        controlPlane.GetIntegrationsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<IntegrationItem>());

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

        // Assert - should NOT have fetched secrets when no integrations are due
        await controlPlane.DidNotReceive().GetSecretsAsync(Arg.Any<CancellationToken>());
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
