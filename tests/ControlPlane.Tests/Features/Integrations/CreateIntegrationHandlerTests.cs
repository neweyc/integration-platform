using ControlPlane.Features.Billing;
using ControlPlane.Features.Environments;
using ControlPlane.Features.Integrations;
using ControlPlane.Features.Tenants;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Integrations;

public class CreateIntegrationHandlerTests
{
    private readonly IIntegrationRepository _repository = Substitute.For<IIntegrationRepository>();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly IEnvironmentReadRepository _environments = Substitute.For<IEnvironmentReadRepository>();
    private readonly ITenantReadRepository _tenants = Substitute.For<ITenantReadRepository>();
    private readonly BillingPlanCatalog _planCatalog = new(new StripeOptions());
    private readonly CreateIntegrationHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();

    public CreateIntegrationHandlerTests()
    {
        _handler = new CreateIntegrationHandler(_repository, _encryption, _environments, _tenants, _planCatalog);
        _environments.ExistsAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _tenants.GetByIdAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Tenant { Id = _tenantId, Plan = BillingPlan.Free });
        _repository.CreateAsync(Arg.Any<Integration>(), Arg.Any<IReadOnlyList<IntegrationTrigger>>())
            .Returns(call =>
            {
                var integration = call.Arg<Integration>();
                foreach (var trigger in call.Arg<IReadOnlyList<IntegrationTrigger>>())
                    integration.Triggers.Add(trigger);
                return integration;
            });
    }

    [Fact]
    public async Task HandleAsync_ValidIntegration_CreatesAndReturnsResult()
    {
        var command = Command(triggers:
        [
            new IntegrationTriggerInput("Run hourly", "hourly", TriggerType.Scheduled, CronExpression: "0 * * * *")
        ]);

        _repository.SlugExistsAsync(_tenantId, "sync-orders").Returns(false);

        var result = await _handler.HandleAsync(command);

        Assert.Equal("Sync Orders", result.Name);
        Assert.Equal("sync-orders", result.Slug);
        Assert.Equal("Enabled", result.Status);
        Assert.Equal("MyCompany.Integrations.SyncOrdersIntegration", result.ClassName);
        var trigger = Assert.Single(result.Triggers);
        Assert.Equal("Scheduled", trigger.Type);
        Assert.Equal("0 * * * *", trigger.CronExpression);
    }

    [Fact]
    public async Task HandleAsync_WebhookTrigger_ReturnsOneTimeSecretAndSigning()
    {
        _encryption.Encrypt(Arg.Any<string>()).Returns("encrypted");
        _repository.GetTenantSlugAsync(_tenantId).Returns("acme");
        var command = Command(triggers:
        [
            new IntegrationTriggerInput("Orders webhook", "orders", TriggerType.Webhook)
        ]);

        var result = await _handler.HandleAsync(command);

        var trigger = Assert.Single(result.Triggers);
        Assert.Equal("Webhook", trigger.Type);
        Assert.StartsWith("whs_", trigger.WebhookSecret);
        Assert.Equal("/webhooks/acme/sync-orders/orders", trigger.WebhookUrl);
        Assert.NotNull(trigger.WebhookSigning);
    }

    [Fact]
    public async Task HandleAsync_ValidTimeout_StoresTimeoutSeconds()
    {
        var command = Command(timeoutSeconds: 300);

        var result = await _handler.HandleAsync(command);

        Assert.Equal(300, result.TimeoutSeconds);
    }

    [Fact]
    public async Task HandleAsync_DuplicateSlug_ThrowsConflictException()
    {
        var command = Command();

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
        var command = Command(name, slug, environment, className: className);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));

        Assert.Equal(expectedMessage, ex.Message);
    }

    [Fact]
    public async Task HandleAsync_ScheduledWithoutCron_ThrowsValidationException()
    {
        var command = Command(triggers:
        [
            new IntegrationTriggerInput("Report", "report", TriggerType.Scheduled)
        ]);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));

        Assert.Equal("A cron expression is required for scheduled triggers.", ex.Message);
    }

    [Fact]
    public async Task HandleAsync_InvalidCronExpression_ThrowsValidationException()
    {
        var command = Command(triggers:
        [
            new IntegrationTriggerInput("Report", "report", TriggerType.Scheduled, CronExpression: "not-a-cron")
        ]);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));
    }

    [Fact]
    public async Task HandleAsync_AtPlanIntegrationCap_ThrowsValidationException()
    {
        // Free plan caps integrations at 10; a tenant already at the cap can't add a net-new one.
        _repository.CountAsync(_tenantId).Returns(10);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(Command()));

        Assert.Contains("limited to 10 integrations", ex.Message);
    }

    [Fact]
    public async Task HandleAsync_BelowPlanIntegrationCap_Succeeds()
    {
        _repository.CountAsync(_tenantId).Returns(9);

        var result = await _handler.HandleAsync(Command());

        Assert.Equal("sync-orders", result.Slug);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task HandleAsync_InvalidTimeout_ThrowsValidationException(int timeoutSeconds)
    {
        var command = Command(timeoutSeconds: timeoutSeconds);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));

        Assert.Equal("Timeout must be greater than zero seconds.", ex.Message);
    }

    private CreateIntegrationCommand Command(
        string name = "Sync Orders",
        string slug = "sync-orders",
        string environment = "production",
        string className = "MyCompany.Integrations.SyncOrdersIntegration",
        IReadOnlyList<IntegrationTriggerInput>? triggers = null,
        int? timeoutSeconds = null) =>
        new(
            _tenantId,
            name,
            slug,
            "Syncs orders from Shopify",
            environment,
            className,
            triggers ?? [],
            timeoutSeconds);
}
