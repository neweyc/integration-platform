using ControlPlane.Features.AgentTokens;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.AgentTokens;

public class PollIntegrationsHandlerTests
{
    private readonly IPollRepository _repository = Substitute.For<IPollRepository>();
    private readonly PollIntegrationsHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();

    public PollIntegrationsHandlerTests()
    {
        _handler = new PollIntegrationsHandler(_repository);
    }

    [Fact]
    public async Task HandleAsync_ReturnsClaimedDueIntegrations()
    {
        var integrationId = Guid.NewGuid();

        _repository
            .ClaimDueScheduledAsync(
                Arg.Is(_tenantId),
                Arg.Is("production"),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns([
                new Integration
                {
                    Id = integrationId,
                    TenantId = _tenantId,
                    Name = "Sync Orders",
                    Slug = "sync-orders",
                    Environment = "production",
                    TriggerType = TriggerType.Scheduled,
                    CronExpression = "0 * * * *",
                    ClassName = "MyCompany.Integrations.SyncOrdersIntegration"
                }
            ]);

        var result = await _handler.HandleAsync(new PollIntegrationsCommand(_tenantId, "production"));

        var integration = Assert.Single(result.Integrations);
        Assert.Equal(integrationId, integration.Id);
        Assert.Equal("Sync Orders", integration.Name);
        Assert.Equal(TriggerType.Scheduled, integration.TriggerType);
        Assert.Equal("0 * * * *", integration.CronExpression);
    }
}
