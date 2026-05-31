using ControlPlane.Features.Integrations;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Integrations;

public class ListExecutionLogsHandlerTests
{
    private readonly IExecutionLogReadRepository _repository = Substitute.For<IExecutionLogReadRepository>();
    private readonly ListExecutionLogsHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _integrationId = Guid.NewGuid();
    private readonly Guid _executionId = Guid.NewGuid();

    public ListExecutionLogsHandlerTests()
    {
        _handler = new ListExecutionLogsHandler(_repository);
    }

    [Fact]
    public async Task HandleAsync_ExecutionBelongsToIntegration_ReturnsLogs()
    {
        var logId = Guid.NewGuid();
        var timestamp = DateTime.UtcNow;

        _repository
            .ExecutionBelongsToIntegrationAsync(_tenantId, _integrationId, _executionId)
            .Returns(true);

        _repository.ListForExecutionAsync(_tenantId, _executionId).Returns([
            new ExecutionLog
            {
                Id = logId,
                TenantId = _tenantId,
                ExecutionRecordId = _executionId,
                Timestamp = timestamp,
                Level = "Warning",
                Message = "Rate limited",
                Exception = null,
                PropertiesJson = """{"RetryAfter":"30"}"""
            }
        ]);

        var result = await _handler.HandleAsync(
            new ListExecutionLogsCommand(_tenantId, _integrationId, _executionId));

        var log = Assert.Single(result.Logs);
        Assert.Equal(logId, log.Id);
        Assert.Equal(timestamp, log.Timestamp);
        Assert.Equal("Warning", log.Level);
        Assert.Equal("Rate limited", log.Message);
        Assert.Equal("""{"RetryAfter":"30"}""", log.PropertiesJson);
    }

    [Fact]
    public async Task HandleAsync_ExecutionNotFound_ThrowsNotFoundException()
    {
        _repository
            .ExecutionBelongsToIntegrationAsync(_tenantId, _integrationId, _executionId)
            .Returns(false);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.HandleAsync(new ListExecutionLogsCommand(_tenantId, _integrationId, _executionId)));
    }
}
