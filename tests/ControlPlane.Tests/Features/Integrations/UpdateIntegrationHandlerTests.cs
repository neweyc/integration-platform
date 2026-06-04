using ControlPlane.Features.Integrations;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Integrations;

public class UpdateIntegrationHandlerTests
{
    private readonly IIntegrationUpdateRepository _repository = Substitute.For<IIntegrationUpdateRepository>();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly UpdateIntegrationHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _integrationId = Guid.NewGuid();

    public UpdateIntegrationHandlerTests()
    {
        _handler = new UpdateIntegrationHandler(_repository, _encryption);
        _repository.UpdateAsync(Arg.Any<Integration>(), Arg.Any<IReadOnlyList<IntegrationTrigger>>())
            .Returns(call =>
            {
                var integration = call.Arg<Integration>();
                integration.Triggers.Clear();
                integration.Triggers.AddRange(call.Arg<IReadOnlyList<IntegrationTrigger>>());
                return integration;
            });
    }

    [Fact]
    public async Task HandleAsync_ValidUpdate_ReturnsUpdatedResult()
    {
        var existing = Existing();
        _repository.GetByIdAsync(_tenantId, _integrationId).Returns(existing);

        var command = Command("New Name", "Updated description", IntegrationStatus.Disabled);

        var result = await _handler.HandleAsync(command);

        Assert.Equal("New Name", result.Name);
        Assert.Equal("Disabled", result.Status);
    }

    [Fact]
    public async Task HandleAsync_ValidTimeout_UpdatesTimeoutSeconds()
    {
        var existing = Existing();
        _repository.GetByIdAsync(_tenantId, _integrationId).Returns(existing);

        var command = Command(timeoutSeconds: 120);

        var result = await _handler.HandleAsync(command);

        Assert.Equal(120, result.TimeoutSeconds);
        Assert.Equal(120, existing.TimeoutSeconds);
    }

    [Fact]
    public async Task HandleAsync_IntegrationNotFound_ThrowsNotFoundException()
    {
        _repository.GetByIdAsync(_tenantId, _integrationId).Returns((Integration?)null);

        var command = Command();

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(command));
    }

    [Fact]
    public async Task HandleAsync_ScheduledTrigger_UpdatesCronExpression()
    {
        var existing = Existing();
        existing.Triggers.Add(new IntegrationTrigger
        {
            TenantId = _tenantId,
            IntegrationId = _integrationId,
            Name = "Schedule",
            Slug = "schedule",
            Type = TriggerType.Scheduled,
            CronExpression = "0 2 * * *"
        });
        _repository.GetByIdAsync(_tenantId, _integrationId).Returns(existing);

        var command = Command(triggers:
        [
            new IntegrationTriggerInput("Schedule", "schedule", TriggerType.Scheduled, CronExpression: "0 6 * * *")
        ]);

        var result = await _handler.HandleAsync(command);

        var trigger = Assert.Single(result.Triggers);
        Assert.Equal("0 6 * * *", trigger.CronExpression);
    }

    [Fact]
    public async Task HandleAsync_ScheduledTriggerWithInvalidCron_ThrowsValidationException()
    {
        _repository.GetByIdAsync(_tenantId, _integrationId).Returns(Existing());

        var command = Command(triggers:
        [
            new IntegrationTriggerInput("Schedule", "schedule", TriggerType.Scheduled, CronExpression: "not-valid")
        ]);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task HandleAsync_InvalidTimeout_ThrowsValidationException(int timeoutSeconds)
    {
        _repository.GetByIdAsync(_tenantId, _integrationId).Returns(Existing());

        var command = Command(timeoutSeconds: timeoutSeconds);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));

        Assert.Equal("Timeout must be greater than zero seconds.", ex.Message);
    }

    private Integration Existing() => new()
    {
        Id = _integrationId,
        TenantId = _tenantId,
        Name = "Integration",
        Slug = "integration",
        Environment = "production",
        Status = IntegrationStatus.Enabled
    };

    private UpdateIntegrationCommand Command(
        string name = "Integration",
        string? description = null,
        IntegrationStatus status = IntegrationStatus.Enabled,
        IReadOnlyList<IntegrationTriggerInput>? triggers = null,
        int? timeoutSeconds = null) =>
        new(
            _tenantId,
            _integrationId,
            name,
            description,
            status,
            triggers ?? [],
            timeoutSeconds);
}
