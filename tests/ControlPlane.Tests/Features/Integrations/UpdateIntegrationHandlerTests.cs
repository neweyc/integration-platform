using ControlPlane.Features.Integrations;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Integrations;

public class UpdateIntegrationHandlerTests
{
    private readonly IIntegrationUpdateRepository _repository = Substitute.For<IIntegrationUpdateRepository>();
    private readonly UpdateIntegrationHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _integrationId = Guid.NewGuid();

    public UpdateIntegrationHandlerTests()
    {
        _handler = new UpdateIntegrationHandler(_repository);
        _repository.UpdateAsync(Arg.Any<Integration>()).Returns(call => call.Arg<Integration>());
    }

    [Fact]
    public async Task HandleAsync_ValidUpdate_ReturnsUpdatedResult()
    {
        var existing = new Integration
        {
            Id = _integrationId,
            TenantId = _tenantId,
            Name = "Old Name",
            Slug = "old-name",
            Environment = "production",
            TriggerType = TriggerType.Manual,
            Status = IntegrationStatus.Enabled
        };

        _repository.GetByIdAsync(_tenantId, _integrationId).Returns(existing);

        var command = new UpdateIntegrationCommand(
            _tenantId, _integrationId, "New Name", "Updated description",
            IntegrationStatus.Disabled, null);

        var result = await _handler.HandleAsync(command);

        Assert.Equal("New Name", result.Name);
        Assert.Equal("Disabled", result.Status);
    }

    [Fact]
    public async Task HandleAsync_ValidTimeout_UpdatesTimeoutSeconds()
    {
        var existing = new Integration
        {
            Id = _integrationId,
            TenantId = _tenantId,
            Name = "Integration",
            Slug = "integration",
            Environment = "production",
            TriggerType = TriggerType.Manual,
            Status = IntegrationStatus.Enabled
        };

        _repository.GetByIdAsync(_tenantId, _integrationId).Returns(existing);

        var command = new UpdateIntegrationCommand(
            _tenantId, _integrationId, "Integration", null,
            IntegrationStatus.Enabled, null, TimeoutSeconds: 120);

        var result = await _handler.HandleAsync(command);

        Assert.Equal(120, result.TimeoutSeconds);
        Assert.Equal(120, existing.TimeoutSeconds);
    }

    [Fact]
    public async Task HandleAsync_IntegrationNotFound_ThrowsNotFoundException()
    {
        _repository.GetByIdAsync(_tenantId, _integrationId).Returns((Integration?)null);

        var command = new UpdateIntegrationCommand(
            _tenantId, _integrationId, "Name", null, IntegrationStatus.Enabled, null);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(command));
    }

    [Fact]
    public async Task HandleAsync_ScheduledIntegration_UpdatesCronExpression()
    {
        var existing = new Integration
        {
            Id = _integrationId,
            TenantId = _tenantId,
            Name = "Report",
            Slug = "report",
            Environment = "production",
            TriggerType = TriggerType.Scheduled,
            CronExpression = "0 2 * * *",
            Status = IntegrationStatus.Enabled
        };

        _repository.GetByIdAsync(_tenantId, _integrationId).Returns(existing);

        var command = new UpdateIntegrationCommand(
            _tenantId, _integrationId, "Report", null,
            IntegrationStatus.Enabled, "0 6 * * *");

        var result = await _handler.HandleAsync(command);

        Assert.Equal("0 6 * * *", result.CronExpression);
    }

    [Fact]
    public async Task HandleAsync_ScheduledIntegrationWithInvalidCron_ThrowsValidationException()
    {
        var existing = new Integration
        {
            Id = _integrationId,
            TenantId = _tenantId,
            TriggerType = TriggerType.Scheduled
        };

        _repository.GetByIdAsync(_tenantId, _integrationId).Returns(existing);

        var command = new UpdateIntegrationCommand(
            _tenantId, _integrationId, "Report", null,
            IntegrationStatus.Enabled, "not-valid");

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task HandleAsync_InvalidTimeout_ThrowsValidationException(int timeoutSeconds)
    {
        var existing = new Integration
        {
            Id = _integrationId,
            TenantId = _tenantId,
            TriggerType = TriggerType.Manual
        };

        _repository.GetByIdAsync(_tenantId, _integrationId).Returns(existing);

        var command = new UpdateIntegrationCommand(
            _tenantId, _integrationId, "Integration", null,
            IntegrationStatus.Enabled, null, TimeoutSeconds: timeoutSeconds);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));

        Assert.Equal("Timeout must be greater than zero seconds.", ex.Message);
    }
}
