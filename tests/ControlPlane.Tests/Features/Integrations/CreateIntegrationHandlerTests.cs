using ControlPlane.Features.Integrations;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Integrations;

public class CreateIntegrationHandlerTests
{
    private readonly IIntegrationRepository _repository = Substitute.For<IIntegrationRepository>();
    private readonly CreateIntegrationHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();

    public CreateIntegrationHandlerTests()
    {
        _handler = new CreateIntegrationHandler(_repository);
        _repository.CreateAsync(Arg.Any<Integration>()).Returns(call => call.Arg<Integration>());
    }

    [Fact]
    public async Task HandleAsync_ValidManualIntegration_CreatesAndReturnsResult()
    {
        var command = new CreateIntegrationCommand(
            _tenantId, "Sync Orders", "sync-orders", "Syncs orders from Shopify",
            "production", TriggerType.Manual, null, "MyCompany.Integrations.SyncOrdersIntegration");

        _repository.SlugExistsAsync(_tenantId, "sync-orders").Returns(false);

        var result = await _handler.HandleAsync(command);

        Assert.Equal("Sync Orders", result.Name);
        Assert.Equal("sync-orders", result.Slug);
        Assert.Equal("Enabled", result.Status);
        Assert.Equal("Manual", result.TriggerType);
        Assert.Equal("MyCompany.Integrations.SyncOrdersIntegration", result.ClassName);
    }

    [Fact]
    public async Task HandleAsync_ValidScheduledIntegration_StoresCronExpression()
    {
        var command = new CreateIntegrationCommand(
            _tenantId, "Nightly Report", "nightly-report", null,
            "production", TriggerType.Scheduled, "0 2 * * *", "MyCompany.Integrations.NightlyReportIntegration");

        _repository.SlugExistsAsync(_tenantId, "nightly-report").Returns(false);

        var result = await _handler.HandleAsync(command);

        Assert.Equal("0 2 * * *", result.CronExpression);
    }

    [Fact]
    public async Task HandleAsync_ValidTimeout_StoresTimeoutSeconds()
    {
        var command = new CreateIntegrationCommand(
            _tenantId, "Sync Orders", "sync-orders", null,
            "production", TriggerType.Manual, null, "MyCompany.Integrations.SyncOrdersIntegration",
            TimeoutSeconds: 300);

        _repository.SlugExistsAsync(_tenantId, "sync-orders").Returns(false);

        var result = await _handler.HandleAsync(command);

        Assert.Equal(300, result.TimeoutSeconds);
    }

    [Fact]
    public async Task HandleAsync_DuplicateSlug_ThrowsConflictException()
    {
        var command = new CreateIntegrationCommand(
            _tenantId, "Sync Orders", "sync-orders", null,
            "production", TriggerType.Manual, null, "MyCompany.Integrations.SyncOrdersIntegration");

        _repository.SlugExistsAsync(_tenantId, "sync-orders").Returns(true);

        await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(command));
    }

    [Theory]
    [InlineData("", "valid-slug", "production", "MyCompany.Test", "Name is required.")]
    [InlineData("Valid Name", "", "production", "MyCompany.Test", "Slug is required.")]
    [InlineData("Valid Name", "Invalid Slug!", "production", "MyCompany.Test", "Slug may only contain lowercase letters, numbers, and hyphens.")]
    [InlineData("Valid Name", "valid-slug", "", "MyCompany.Test", "Environment is required.")]
    [InlineData("Valid Name", "valid-slug", "production", "", "Class name is required.")]
    [InlineData("Valid Name", "valid-slug", "production", "Invalid Class Name!", "Class name must be a valid fully-qualified .NET type name (e.g. 'MyCompany.Integrations.SyncOrdersIntegration').")]
    public async Task HandleAsync_InvalidInput_ThrowsValidationException(
        string name, string slug, string environment, string className, string expectedMessage)
    {
        var command = new CreateIntegrationCommand(
            _tenantId, name, slug, null, environment, TriggerType.Manual, null, className);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));

        Assert.Equal(expectedMessage, ex.Message);
    }

    [Fact]
    public async Task HandleAsync_ScheduledWithoutCron_ThrowsValidationException()
    {
        var command = new CreateIntegrationCommand(
            _tenantId, "Report", "report", null,
            "production", TriggerType.Scheduled, null, "MyCompany.Integrations.ReportIntegration");

        _repository.SlugExistsAsync(_tenantId, "report").Returns(false);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));

        Assert.Equal("A cron expression is required for scheduled integrations.", ex.Message);
    }

    [Fact]
    public async Task HandleAsync_InvalidCronExpression_ThrowsValidationException()
    {
        var command = new CreateIntegrationCommand(
            _tenantId, "Report", "report", null,
            "production", TriggerType.Scheduled, "not-a-cron", "MyCompany.Integrations.ReportIntegration");

        _repository.SlugExistsAsync(_tenantId, "report").Returns(false);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task HandleAsync_InvalidTimeout_ThrowsValidationException(int timeoutSeconds)
    {
        var command = new CreateIntegrationCommand(
            _tenantId, "Sync Orders", "sync-orders", null,
            "production", TriggerType.Manual, null, "MyCompany.Integrations.SyncOrdersIntegration",
            TimeoutSeconds: timeoutSeconds);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));

        Assert.Equal("Timeout must be greater than zero seconds.", ex.Message);
    }
}
