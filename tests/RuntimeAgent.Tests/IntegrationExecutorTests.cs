using IntegrationPlatform.Sdk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RuntimeAgent.Agent;
using RuntimeAgent.Execution;

namespace RuntimeAgent.Tests;

public class IntegrationExecutorTests
{
    private readonly IControlPlaneClient _controlPlane = Substitute.For<IControlPlaneClient>();
    private readonly IntegrationLoader _loader;
    private readonly IHttpClientFactory _httpClientFactory = Substitute.For<IHttpClientFactory>();
    private readonly AgentOptions _options = new() { Environment = "production" };
    private readonly IntegrationExecutor _executor;

    public IntegrationExecutorTests()
    {
        _loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);
        _httpClientFactory.CreateClient("integration").Returns(new HttpClient());
        _executor = new IntegrationExecutor(
            _controlPlane,
            _loader,
            _httpClientFactory,
            _options,
            NullLogger<IntegrationExecutor>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownClassName_SkipsExecution()
    {
        var integration = new IntegrationItem(
            Guid.NewGuid(), "Test Integration", "test-integration",
            "Scheduled", "0 * * * *", "Unknown.ClassName",
            DateTime.UtcNow.AddMinutes(5), "Scheduled", null);

        await _executor.ExecuteAsync(integration, new Dictionary<string, string>(), CancellationToken.None);

        await _controlPlane.DidNotReceive().StartExecutionAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulRun_ReportsSuccess()
    {
        var executionId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var integration = new IntegrationItem(
            integrationId, "Test Integration", "test-integration",
            "Scheduled", "0 * * * *", typeof(SuccessfulTestIntegration).FullName!,
            DateTime.UtcNow.AddMinutes(5), "Scheduled", null);

        _controlPlane.StartExecutionAsync(integrationId, Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(executionId);
        _loader.LoadFromDirectory(AppContext.BaseDirectory);

        await _executor.ExecuteAsync(integration, new Dictionary<string, string> { ["API_KEY"] = "test-key" }, CancellationToken.None);

        await _controlPlane.Received(1).StartExecutionAsync(integrationId, Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _controlPlane.Received(1).CompleteExecutionAsync(
            executionId, succeeded: true, errorMessage: null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_FailingRun_ReportsFailure()
    {
        var executionId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var integration = new IntegrationItem(
            integrationId, "Failing Integration", "failing-integration",
            "Scheduled", "0 * * * *", typeof(FailingTestIntegration).FullName!,
            DateTime.UtcNow.AddMinutes(5), "Scheduled", null);

        _controlPlane.StartExecutionAsync(integrationId, Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(executionId);
        _loader.LoadFromDirectory(AppContext.BaseDirectory);

        await _executor.ExecuteAsync(integration, new Dictionary<string, string>(), CancellationToken.None);

        await _controlPlane.Received(1).CompleteExecutionAsync(
            executionId, succeeded: false,
            Arg.Is<string>(msg => msg.Contains("Integration failed intentionally")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_TimeoutExpires_ReportsTimedOut()
    {
        SlowTestIntegration.ExecutionStarted = new TaskCompletionSource();

        var executionId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var integration = new IntegrationItem(
            integrationId, "Slow Integration", "slow",
            "Scheduled", "0 * * * *", typeof(SlowTestIntegration).FullName!,
            DateTime.UtcNow.AddMinutes(5), "Scheduled", null,
            TimeoutSeconds: 1);

        _controlPlane.StartExecutionAsync(integrationId, Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(executionId);
        _loader.LoadFromDirectory(AppContext.BaseDirectory);

        await _executor.ExecuteAsync(integration, new Dictionary<string, string>(), CancellationToken.None);

        await _controlPlane.Received(1).CompleteExecutionAsync(
            executionId, succeeded: false,
            Arg.Is<string?>(msg => msg != null && msg.Contains("timed out")),
            Arg.Any<CancellationToken>(),
            isTimeout: true);
    }

    [Fact]
    public async Task ExecuteAsync_NoTimeoutConfigured_CompletesNormally()
    {
        var executionId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var integration = new IntegrationItem(
            integrationId, "Test Integration", "test",
            "Scheduled", "0 * * * *", typeof(SuccessfulTestIntegration).FullName!,
            DateTime.UtcNow.AddMinutes(5), "Scheduled", null,
            TimeoutSeconds: null);

        _controlPlane.StartExecutionAsync(integrationId, Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(executionId);
        _loader.LoadFromDirectory(AppContext.BaseDirectory);

        await _executor.ExecuteAsync(integration, new Dictionary<string, string>(), CancellationToken.None);

        await _controlPlane.Received(1).CompleteExecutionAsync(
            executionId, succeeded: true, errorMessage: null,
            Arg.Any<CancellationToken>(), isTimeout: false);
    }

    [Fact]
    public async Task ExecuteAsync_IntegrationCancelsBeforeTimeout_DoesNotReportTimedOut()
    {
        var executionId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var integration = new IntegrationItem(
            integrationId, "Self Cancelling Integration", "self-cancelling",
            "Scheduled", "0 * * * *", typeof(SelfCancellingTestIntegration).FullName!,
            DateTime.UtcNow.AddMinutes(5), "Scheduled", null,
            TimeoutSeconds: 60);

        _controlPlane.StartExecutionAsync(integrationId, Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(executionId);
        _loader.LoadFromDirectory(AppContext.BaseDirectory);

        await _executor.ExecuteAsync(integration, new Dictionary<string, string>(), CancellationToken.None);

        await _controlPlane.DidNotReceive().CompleteExecutionAsync(
            executionId,
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>(),
            isTimeout: true);
    }
}

// Test integration that succeeds
public class SuccessfulTestIntegration : IIntegration
{
    public Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(context.Execution.Environment))
            throw new InvalidOperationException("Environment should be set");

        return Task.CompletedTask;
    }
}

// Test integration that fails
public class FailingTestIntegration : IIntegration
{
    public Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        throw new InvalidOperationException("Integration failed intentionally");
    }
}

public class SelfCancellingTestIntegration : IIntegration
{
    public Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        throw new OperationCanceledException(new CancellationToken(canceled: true));
    }
}

// Test integration that blocks until cancelled
public class SlowTestIntegration : IIntegration
{
    public static TaskCompletionSource ExecutionStarted = new();

    public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        ExecutionStarted.TrySetResult();
        context.Logger.LogInformation("Slow integration started");
        await Task.Delay(Timeout.Infinite, ct);
    }
}
