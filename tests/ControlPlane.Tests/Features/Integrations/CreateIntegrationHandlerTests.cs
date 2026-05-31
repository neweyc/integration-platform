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
            "production", TriggerType.Manual, null);

        _repository.SlugExistsAsync(_tenantId, "sync-orders").Returns(false);

        var result = await _handler.HandleAsync(command);

        Assert.Equal("Sync Orders", result.Name);
        Assert.Equal("sync-orders", result.Slug);
        Assert.Equal("Enabled", result.Status);
        Assert.Equal("Manual", result.TriggerType);
    }

    [Fact]
    public async Task HandleAsync_ValidScheduledIntegration_StoresCronExpression()
    {
        var command = new CreateIntegrationCommand(
            _tenantId, "Nightly Report", "nightly-report", null,
            "production", TriggerType.Scheduled, "0 2 * * *");

        _repository.SlugExistsAsync(_tenantId, "nightly-report").Returns(false);

        var result = await _handler.HandleAsync(command);

        Assert.Equal("0 2 * * *", result.CronExpression);
    }

    [Fact]
    public async Task HandleAsync_DuplicateSlug_ThrowsConflictException()
    {
        var command = new CreateIntegrationCommand(
            _tenantId, "Sync Orders", "sync-orders", null,
            "production", TriggerType.Manual, null);

        _repository.SlugExistsAsync(_tenantId, "sync-orders").Returns(true);

        await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(command));
    }

    [Theory]
    [InlineData("", "valid-slug", "Name is required.")]
    [InlineData("Valid Name", "", "Slug is required.")]
    [InlineData("Valid Name", "Invalid Slug!", "Slug may only contain lowercase letters, numbers, and hyphens.")]
    [InlineData("Valid Name", "valid-slug", "Environment is required.")]
    public async Task HandleAsync_InvalidInput_ThrowsValidationException(
        string name, string slug, string expectedMessage)
    {
        // Use empty environment to trigger the environment validation in the last case
        var environment = expectedMessage.Contains("Environment") ? "" : "production";

        var command = new CreateIntegrationCommand(
            _tenantId, name, slug, null, environment, TriggerType.Manual, null);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));

        Assert.Equal(expectedMessage, ex.Message);
    }

    [Fact]
    public async Task HandleAsync_ScheduledWithoutCron_ThrowsValidationException()
    {
        var command = new CreateIntegrationCommand(
            _tenantId, "Report", "report", null,
            "production", TriggerType.Scheduled, null);

        _repository.SlugExistsAsync(_tenantId, "report").Returns(false);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));

        Assert.Equal("A cron expression is required for scheduled integrations.", ex.Message);
    }

    [Fact]
    public async Task HandleAsync_InvalidCronExpression_ThrowsValidationException()
    {
        var command = new CreateIntegrationCommand(
            _tenantId, "Report", "report", null,
            "production", TriggerType.Scheduled, "not-a-cron");

        _repository.SlugExistsAsync(_tenantId, "report").Returns(false);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));
    }
}
