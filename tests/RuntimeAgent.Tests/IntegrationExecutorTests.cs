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
        // Arrange
        var integration = new IntegrationItem(
            Guid.NewGuid(),
            "Test Integration",
            "test-integration",
            "Scheduled",
            "0 * * * *",
            "Unknown.ClassName",
            DateTime.UtcNow.AddMinutes(5),
            "Scheduled",
            null);
        var secrets = new Dictionary<string, string>();

        // Act
        await _executor.ExecuteAsync(integration, secrets, CancellationToken.None);

        // Assert - should not call StartExecutionAsync since class was not found
        await _controlPlane.DidNotReceive().StartExecutionAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulRun_ReportsSuccess()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var integration = new IntegrationItem(
            integrationId,
            "Test Integration",
            "test-integration",
            "Scheduled",
            "0 * * * *",
            typeof(SuccessfulTestIntegration).FullName!,
            DateTime.UtcNow.AddMinutes(5),
            "Scheduled",
            null);

        var secrets = new Dictionary<string, string> { ["API_KEY"] = "test-key" };

        _controlPlane.StartExecutionAsync(integrationId, Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(executionId);

        // Load the test assembly so the loader can find our test integration
        _loader.LoadFromDirectory(AppContext.BaseDirectory);

        // Act
        await _executor.ExecuteAsync(integration, secrets, CancellationToken.None);

        // Assert
        await _controlPlane.Received(1).StartExecutionAsync(integrationId, Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _controlPlane.Received(1).CompleteExecutionAsync(
            executionId,
            succeeded: true,
            errorMessage: null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_FailingRun_ReportsFailure()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var integration = new IntegrationItem(
            integrationId,
            "Failing Integration",
            "failing-integration",
            "Scheduled",
            "0 * * * *",
            typeof(FailingTestIntegration).FullName!,
            DateTime.UtcNow.AddMinutes(5),
            "Scheduled",
            null);

        var secrets = new Dictionary<string, string>();

        _controlPlane.StartExecutionAsync(integrationId, Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(executionId);

        // Load the test assembly
        _loader.LoadFromDirectory(AppContext.BaseDirectory);

        // Act
        await _executor.ExecuteAsync(integration, secrets, CancellationToken.None);

        // Assert
        await _controlPlane.Received(1).StartExecutionAsync(integrationId, Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _controlPlane.Received(1).CompleteExecutionAsync(
            executionId,
            succeeded: false,
            Arg.Is<string>(msg => msg.Contains("Integration failed intentionally")),
            Arg.Any<CancellationToken>());
    }
}

// Test integration that succeeds
public class SuccessfulTestIntegration : IIntegration
{
    public Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        // Verify context is properly populated
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
