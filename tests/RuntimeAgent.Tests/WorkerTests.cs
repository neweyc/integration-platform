using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RuntimeAgent.Agent;
using RuntimeAgent.Execution;

namespace RuntimeAgent.Tests;

public class WorkerTests
{
    private static PackageSyncer NoOpSyncer(IControlPlaneClient controlPlane, AgentOptions options)
    {
        var loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);
        return new PackageSyncer(controlPlane, loader, options, NullLogger<PackageSyncer>.Instance);
    }


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

        var worker = new Worker(controlPlane, executor, loader, NoOpSyncer(controlPlane, options), options,
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
            leaseExpires,
            "Scheduled",
            null,
            WorkItemId: Guid.NewGuid());

        controlPlane.GetIntegrationsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<IntegrationItem> { integration });
        controlPlane.GetSecretsAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string>());
        controlPlane.StartExecutionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        var worker = new Worker(controlPlane, executor, loader, NoOpSyncer(controlPlane, options), options,
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
        await controlPlane.Received().StartExecutionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
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
            DateTime.UtcNow.AddMinutes(5),
            "Scheduled",
            null);

        controlPlane.GetIntegrationsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<IntegrationItem> { integration });
        controlPlane.GetSecretsAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string> { ["API_KEY"] = "secret" });

        var worker = new Worker(controlPlane, executor, loader, NoOpSyncer(controlPlane, options), options,
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

        var worker = new Worker(controlPlane, executor, loader, NoOpSyncer(controlPlane, options), options,
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

        var worker = new Worker(controlPlane, executor, loader, NoOpSyncer(controlPlane, options), options,
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
    public async Task Worker_Shutdown_CancelledExecution_ReportsFailureToControlPlane()
    {
        // Arrange
        SlowTestIntegration.ExecutionStarted = new TaskCompletionSource();

        var controlPlane = Substitute.For<IControlPlaneClient>();
        var loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient("integration").Returns(new HttpClient());

        var options = new AgentOptions
        {
            Environment = "production",
            PollIntervalSeconds = 1,
            MaxConcurrentExecutions = 2,
            IntegrationsPath = AppContext.BaseDirectory,
            ShutdownDrainSeconds = 0  // immediate cancellation after drain window
        };

        var executor = new IntegrationExecutor(
            controlPlane, loader, httpFactory, options,
            NullLogger<IntegrationExecutor>.Instance);

        var integrationId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        var integration = new IntegrationItem(
            integrationId, "Slow Integration", "slow",
            "Scheduled", "0 * * * *",
            typeof(SlowTestIntegration).FullName!,
            DateTime.UtcNow.AddMinutes(5), "Scheduled", null,
            WorkItemId: Guid.NewGuid());

        controlPlane.GetIntegrationsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<IntegrationItem> { integration });
        controlPlane.GetSecretsAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string>());
        controlPlane.StartExecutionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(executionId);

        var worker = new Worker(controlPlane, executor, loader, NoOpSyncer(controlPlane, options), options,
            NullLogger<Worker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        // Wait until the slow integration has actually started executing
        await SlowTestIntegration.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act — trigger shutdown
        await worker.StopAsync(CancellationToken.None);

        // Assert — execution should be reported as failed with a shutdown message
        await controlPlane.Received(1).RecordLogAsync(
            executionId,
            Arg.Is<ExecutionLogEntry>(log => log.Message.Contains("Slow integration started")),
            Arg.Any<CancellationToken>());
        await controlPlane.Received(1).CompleteExecutionAsync(
            executionId,
            succeeded: false,
            Arg.Is<string?>(msg => msg != null && msg.Contains("cancelled")),
            Arg.Any<CancellationToken>(),
            retryable: false);
    }

    [Fact]
    public async Task Worker_Shutdown_InFlightExecution_CompletesWithinDrainWindow()
    {
        // Arrange
        SlowTestIntegration.ExecutionStarted = new TaskCompletionSource();

        var controlPlane = Substitute.For<IControlPlaneClient>();
        var loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient("integration").Returns(new HttpClient());

        var options = new AgentOptions
        {
            Environment = "production",
            PollIntervalSeconds = 60,
            MaxConcurrentExecutions = 2,
            IntegrationsPath = AppContext.BaseDirectory,
            ShutdownDrainSeconds = 5  // allow time to finish
        };

        var executor = new IntegrationExecutor(
            controlPlane, loader, httpFactory, options,
            NullLogger<IntegrationExecutor>.Instance);

        var integrationId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        var integration = new IntegrationItem(
            integrationId, "Sync Orders", "sync-orders",
            "Scheduled", "0 * * * *",
            typeof(SuccessfulTestIntegration).FullName!,
            DateTime.UtcNow.AddMinutes(5), "Scheduled", null,
            WorkItemId: Guid.NewGuid());

        controlPlane.GetIntegrationsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<IntegrationItem> { integration });
        controlPlane.GetSecretsAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string>());
        controlPlane.StartExecutionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(executionId);

        var worker = new Worker(controlPlane, executor, loader, NoOpSyncer(controlPlane, options), options,
            NullLogger<Worker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(150, CancellationToken.None);

        // Act
        await worker.StopAsync(CancellationToken.None);

        // Assert — execution completed successfully (not cancelled)
        await controlPlane.Received().CompleteExecutionAsync(
            executionId,
            succeeded: true,
            errorMessage: null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AgentOptions_DefaultValues_AreReasonable()
    {
        var options = new AgentOptions();

        Assert.Equal(30, options.PollIntervalSeconds);
        Assert.Equal(5, options.MaxConcurrentExecutions);
        Assert.Equal(30, options.ShutdownDrainSeconds);
        Assert.Equal("", options.ControlPlaneUrl);
        Assert.Equal("", options.AgentToken);
        Assert.Equal("", options.Environment);
        Assert.Equal("", options.IntegrationsPath);
    }
}
