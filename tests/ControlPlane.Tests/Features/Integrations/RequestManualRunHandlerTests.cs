using ControlPlane.Features.Integrations;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Integrations;

public class RequestManualRunHandlerTests
{
    private readonly IManualRunRepository _repository = Substitute.For<IManualRunRepository>();
    private readonly RequestManualRunHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _integrationId = Guid.NewGuid();

    public RequestManualRunHandlerTests()
    {
        _handler = new RequestManualRunHandler(_repository);
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_CreatesManualRunRequest()
    {
        var integration = new Integration
        {
            Id = _integrationId,
            TenantId = _tenantId,
            Name = "Sync Orders",
            Environment = "production",
            Status = IntegrationStatus.Enabled
        };

        _repository.GetIntegrationAsync(_tenantId, _integrationId).Returns(integration);
        _repository.HasPendingRunAsync(_tenantId, _integrationId).Returns(false);
        _repository.HasRunningExecutionAsync(_tenantId, _integrationId).Returns(false);
        _repository.CreateAsync(Arg.Any<ManualRunRequest>()).Returns(call =>
        {
            var request = call.Arg<ManualRunRequest>();
            return request;
        });

        var command = new RequestManualRunCommand(_tenantId, _integrationId);
        var result = await _handler.HandleAsync(command);

        Assert.Equal(_integrationId, result.IntegrationId);
        Assert.Equal("Sync Orders", result.IntegrationName);
        Assert.Equal("production", result.Environment);

        await _repository.Received(1).CreateWorkItemAsync(
            Arg.Is<WorkItem>(w =>
                w.TenantId == _tenantId
                && w.IntegrationId == _integrationId
                && w.Environment == "production"
                && w.TriggerSource == TriggerSource.Manual
                && w.Status == WorkItemStatus.Pending
                && w.ManualRunRequestId == result.RequestId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_IntegrationNotFound_ThrowsNotFoundException()
    {
        _repository.GetIntegrationAsync(_tenantId, _integrationId).Returns((Integration?)null);

        var command = new RequestManualRunCommand(_tenantId, _integrationId);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(command));
        Assert.Contains(_integrationId.ToString(), ex.Message);
    }

    [Fact]
    public async Task HandleAsync_DisabledIntegration_ThrowsValidationException()
    {
        var integration = new Integration
        {
            Id = _integrationId,
            TenantId = _tenantId,
            Name = "Sync Orders",
            Status = IntegrationStatus.Disabled
        };

        _repository.GetIntegrationAsync(_tenantId, _integrationId).Returns(integration);

        var command = new RequestManualRunCommand(_tenantId, _integrationId);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));
        Assert.Contains("disabled", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_PendingRunExists_ThrowsConflictException()
    {
        var integration = new Integration
        {
            Id = _integrationId,
            TenantId = _tenantId,
            Name = "Sync Orders",
            Status = IntegrationStatus.Enabled
        };

        _repository.GetIntegrationAsync(_tenantId, _integrationId).Returns(integration);
        _repository.HasPendingRunAsync(_tenantId, _integrationId).Returns(true);

        var command = new RequestManualRunCommand(_tenantId, _integrationId);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(command));
        Assert.Contains("already pending", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_RunningExecutionExists_ThrowsConflictException()
    {
        var integration = new Integration
        {
            Id = _integrationId,
            TenantId = _tenantId,
            Name = "Sync Orders",
            Status = IntegrationStatus.Enabled
        };

        _repository.GetIntegrationAsync(_tenantId, _integrationId).Returns(integration);
        _repository.HasPendingRunAsync(_tenantId, _integrationId).Returns(false);
        _repository.HasRunningExecutionAsync(_tenantId, _integrationId).Returns(true);

        var command = new RequestManualRunCommand(_tenantId, _integrationId);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(command));
        Assert.Contains("already running", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
