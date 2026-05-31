using ControlPlane.Features.Integrations;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Integrations;

public class ListIntegrationExecutionsHandlerTests
{
    private readonly IIntegrationReadRepository _integrationRepository = Substitute.For<IIntegrationReadRepository>();
    private readonly IExecutionHistoryRepository _executionRepository = Substitute.For<IExecutionHistoryRepository>();
    private readonly ListIntegrationExecutionsHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _integrationId = Guid.NewGuid();

    public ListIntegrationExecutionsHandlerTests()
    {
        _handler = new ListIntegrationExecutionsHandler(_integrationRepository, _executionRepository);
    }

    [Fact]
    public async Task HandleAsync_IntegrationExists_ReturnsExecutions()
    {
        var startedAt = DateTime.UtcNow.AddMinutes(-5);
        var completedAt = startedAt.AddSeconds(42);

        _integrationRepository.GetByIdAsync(_tenantId, _integrationId).Returns(new Integration
        {
            Id = _integrationId,
            TenantId = _tenantId,
            Name = "Sync Orders"
        });

        _executionRepository
            .ListForIntegrationAsync(_tenantId, _integrationId, 25)
            .Returns([
                new ExecutionRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantId,
                    IntegrationId = _integrationId,
                    Environment = "production",
                    Status = ExecutionStatus.Succeeded,
                    StartedAt = startedAt,
                    CompletedAt = completedAt
                }
            ]);

        var result = await _handler.HandleAsync(
            new ListIntegrationExecutionsCommand(_tenantId, _integrationId, 25));

        var execution = Assert.Single(result.Executions);
        Assert.Equal("Succeeded", execution.Status);
        Assert.Equal("production", execution.Environment);
        Assert.Equal(42_000, execution.DurationMs);
    }

    [Fact]
    public async Task HandleAsync_IntegrationNotFound_ThrowsNotFoundException()
    {
        _integrationRepository.GetByIdAsync(_tenantId, _integrationId).Returns((Integration?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.HandleAsync(new ListIntegrationExecutionsCommand(_tenantId, _integrationId, 25)));
    }
}
