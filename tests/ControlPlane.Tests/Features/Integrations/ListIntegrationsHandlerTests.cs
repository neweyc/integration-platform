using ControlPlane.Features.Integrations;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Integrations;

public class ListIntegrationsHandlerTests
{
    private readonly IIntegrationReadRepository _integrationRepository = Substitute.For<IIntegrationReadRepository>();
    private readonly IExecutionHistoryRepository _executionRepository = Substitute.For<IExecutionHistoryRepository>();
    private readonly ListIntegrationsHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();

    public ListIntegrationsHandlerTests()
    {
        _handler = new ListIntegrationsHandler(_integrationRepository, _executionRepository);
    }

    [Fact]
    public async Task HandleAsync_IncludesLatestExecution()
    {
        var integrationId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        _integrationRepository.ListAsync(_tenantId, "production").Returns([
            new Integration
            {
                Id = integrationId,
                TenantId = _tenantId,
                Name = "Sync Orders",
                Slug = "sync-orders",
                Environment = "production",
                Status = IntegrationStatus.Enabled,
                ClassName = "MyCompany.Integrations.SyncOrdersIntegration",
                TimeoutSeconds = 300,
                PackageId = packageId,
                Triggers =
                [
                    new IntegrationTrigger
                    {
                        TenantId = _tenantId,
                        IntegrationId = integrationId,
                        Name = "Hourly",
                        Slug = "hourly",
                        Type = TriggerType.Scheduled,
                        CronExpression = "0 * * * *"
                    }
                ]
            }
        ]);

        _executionRepository.GetLatestForIntegrationsAsync(
                _tenantId,
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == integrationId))
            .Returns(new Dictionary<Guid, ExecutionRecord>
            {
                [integrationId] = new()
                {
                    Id = executionId,
                    TenantId = _tenantId,
                    IntegrationId = integrationId,
                    Environment = "production",
                    Status = ExecutionStatus.Failed,
                    StartedAt = DateTime.UtcNow,
                    ErrorMessage = "Downstream API failed."
                }
            });

        var result = await _handler.HandleAsync(new ListIntegrationsCommand(_tenantId, "production"));

        var integration = Assert.Single(result.Integrations);
        Assert.Equal(integrationId, integration.Id);
        Assert.Equal("Sync Orders", integration.Name);
        Assert.NotNull(integration.LastExecution);
        Assert.Equal(executionId, integration.LastExecution.Id);
        Assert.Equal("Failed", integration.LastExecution.Status);
        Assert.Equal(300, integration.TimeoutSeconds);
        Assert.Equal(packageId, integration.PackageId);
    }
}
