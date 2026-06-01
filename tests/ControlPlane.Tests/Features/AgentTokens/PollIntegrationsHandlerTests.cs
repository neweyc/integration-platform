using ControlPlane.Features.AgentTokens;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.AgentTokens;

public class PollIntegrationsHandlerTests
{
    private readonly IPollRepository _repository = Substitute.For<IPollRepository>();
    private readonly PollIntegrationsHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _leaseOwnerId = Guid.NewGuid();

    public PollIntegrationsHandlerTests()
    {
        _handler = new PollIntegrationsHandler(_repository);
    }

    [Fact]
    public async Task HandleAsync_ReturnsClaimedDueIntegrations()
    {
        var integrationId = Guid.NewGuid();
        var leaseExpiresAt = DateTime.UtcNow.AddMinutes(5);
        var integration = new Integration
        {
            Id = integrationId,
            TenantId = _tenantId,
            Name = "Sync Orders",
            Slug = "sync-orders",
            Environment = "production",
            TriggerType = TriggerType.Scheduled,
            CronExpression = "0 * * * *",
            ClassName = "MyCompany.Integrations.SyncOrdersIntegration"
        };

        _repository
            .ClaimDueScheduledAsync(
                Arg.Is(_tenantId),
                Arg.Is("production"),
                Arg.Is(_leaseOwnerId),
                Arg.Any<TimeSpan>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns([new ClaimedIntegration(integration, leaseExpiresAt)]);

        var result = await _handler.HandleAsync(
            new PollIntegrationsCommand(_tenantId, "production", _leaseOwnerId));

        var item = Assert.Single(result.Integrations);
        Assert.Equal(integrationId, item.Id);
        Assert.Equal("Sync Orders", item.Name);
        Assert.Equal(TriggerType.Scheduled, item.TriggerType);
        Assert.Equal("0 * * * *", item.CronExpression);
        Assert.Equal(leaseExpiresAt, item.LeaseExpiresAt);
    }

    [Fact]
    public async Task HandleAsync_PassesLeaseOwnerIdToRepository()
    {
        _repository
            .ClaimDueScheduledAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        await _handler.HandleAsync(
            new PollIntegrationsCommand(_tenantId, "production", _leaseOwnerId));

        await _repository.Received(1).ClaimDueScheduledAsync(
            Arg.Is(_tenantId),
            Arg.Is("production"),
            Arg.Is(_leaseOwnerId),
            Arg.Any<TimeSpan>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_EmptyListWhenNothingDue()
    {
        _repository
            .ClaimDueScheduledAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _handler.HandleAsync(
            new PollIntegrationsCommand(_tenantId, "production", _leaseOwnerId));

        Assert.Empty(result.Integrations);
    }
}
