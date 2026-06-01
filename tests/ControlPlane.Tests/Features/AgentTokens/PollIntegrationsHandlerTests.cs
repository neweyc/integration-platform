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

        _repository
            .ClaimPendingManualRunsAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _handler.HandleAsync(
            new PollIntegrationsCommand(_tenantId, "production", _leaseOwnerId));

        var item = Assert.Single(result.Integrations);
        Assert.Equal(integrationId, item.Id);
        Assert.Equal("Sync Orders", item.Name);
        Assert.Equal(TriggerType.Scheduled, item.TriggerType);
        Assert.Equal("0 * * * *", item.CronExpression);
        Assert.Equal(leaseExpiresAt, item.LeaseExpiresAt);
        Assert.Equal(TriggerSource.Scheduled, item.TriggerSource);
        Assert.Null(item.ManualRunRequestId);
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

        _repository
            .ClaimPendingManualRunsAsync(
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

        _repository
            .ClaimPendingManualRunsAsync(
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

    [Fact]
    public async Task HandleAsync_ReturnsClaimedManualRuns()
    {
        var integrationId = Guid.NewGuid();
        var manualRunId = Guid.NewGuid();
        var claimExpiresAt = DateTime.UtcNow.AddMinutes(5);
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

        var manualRunRequest = new ManualRunRequest
        {
            Id = manualRunId,
            TenantId = _tenantId,
            IntegrationId = integrationId,
            Environment = "production",
            Status = ManualRunStatus.Claimed,
            ClaimExpiresAt = claimExpiresAt
        };

        _repository
            .ClaimDueScheduledAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        _repository
            .ClaimPendingManualRunsAsync(
                Arg.Is(_tenantId),
                Arg.Is("production"),
                Arg.Is(_leaseOwnerId),
                Arg.Any<TimeSpan>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns([new ClaimedManualRun(manualRunRequest, integration)]);

        var result = await _handler.HandleAsync(
            new PollIntegrationsCommand(_tenantId, "production", _leaseOwnerId));

        var item = Assert.Single(result.Integrations);
        Assert.Equal(integrationId, item.Id);
        Assert.Equal("Sync Orders", item.Name);
        Assert.Equal(TriggerSource.Manual, item.TriggerSource);
        Assert.Equal(manualRunId, item.ManualRunRequestId);
        Assert.Equal(claimExpiresAt, item.LeaseExpiresAt);
    }

    [Fact]
    public async Task HandleAsync_CombinesBothScheduledAndManualRuns()
    {
        var scheduledIntegrationId = Guid.NewGuid();
        var manualIntegrationId = Guid.NewGuid();
        var manualRunId = Guid.NewGuid();
        var leaseExpiresAt = DateTime.UtcNow.AddMinutes(5);

        var scheduledIntegration = new Integration
        {
            Id = scheduledIntegrationId,
            TenantId = _tenantId,
            Name = "Scheduled Job",
            Slug = "scheduled-job",
            Environment = "production",
            TriggerType = TriggerType.Scheduled,
            CronExpression = "0 * * * *",
            ClassName = "MyCompany.ScheduledJob"
        };

        var manualIntegration = new Integration
        {
            Id = manualIntegrationId,
            TenantId = _tenantId,
            Name = "Manual Job",
            Slug = "manual-job",
            Environment = "production",
            TriggerType = TriggerType.Manual,
            ClassName = "MyCompany.ManualJob"
        };

        var manualRunRequest = new ManualRunRequest
        {
            Id = manualRunId,
            TenantId = _tenantId,
            IntegrationId = manualIntegrationId,
            Environment = "production",
            Status = ManualRunStatus.Claimed,
            ClaimExpiresAt = leaseExpiresAt
        };

        _repository
            .ClaimDueScheduledAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns([new ClaimedIntegration(scheduledIntegration, leaseExpiresAt)]);

        _repository
            .ClaimPendingManualRunsAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns([new ClaimedManualRun(manualRunRequest, manualIntegration)]);

        var result = await _handler.HandleAsync(
            new PollIntegrationsCommand(_tenantId, "production", _leaseOwnerId));

        Assert.Equal(2, result.Integrations.Count);
        Assert.Contains(result.Integrations, i => i.TriggerSource == TriggerSource.Scheduled);
        Assert.Contains(result.Integrations, i => i.TriggerSource == TriggerSource.Manual);
    }
}
