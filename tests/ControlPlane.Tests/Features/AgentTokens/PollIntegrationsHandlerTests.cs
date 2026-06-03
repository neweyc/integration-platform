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
        var workItemId = Guid.NewGuid();
        var claimExpiresAt = DateTime.UtcNow.AddMinutes(5);

        var integration = MakeIntegration(integrationId);
        var workItem = MakeWorkItem(workItemId, integrationId, TriggerSource.Scheduled, claimExpiresAt);

        SetupScheduled([new ClaimedWork(integration, workItem)]);
        SetupManual([]);
        SetupWebhook([]);
        SetupRetry([]);

        var result = await _handler.HandleAsync(
            new PollIntegrationsCommand(_tenantId, "production", _leaseOwnerId));

        var item = Assert.Single(result.Integrations);
        Assert.Equal(integrationId, item.Id);
        Assert.Equal("Sync Orders", item.Name);
        Assert.Equal(TriggerType.Scheduled, item.TriggerType);
        Assert.Equal(claimExpiresAt, item.LeaseExpiresAt);
        Assert.Equal(TriggerSource.Scheduled, item.TriggerSource);
        Assert.Null(item.ManualRunRequestId);
        Assert.Equal(workItemId, item.WorkItemId);
    }

    [Fact]
    public async Task HandleAsync_PassesLeaseOwnerIdToRepository()
    {
        SetupScheduled([]);
        SetupManual([]);
        SetupWebhook([]);
        SetupRetry([]);

        await _handler.HandleAsync(new PollIntegrationsCommand(_tenantId, "production", _leaseOwnerId));

        await _repository.Received(1).ClaimDueScheduledAsync(
            _tenantId, "production", _leaseOwnerId,
            Arg.Any<TimeSpan>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_EmptyListWhenNothingDue()
    {
        SetupScheduled([]);
        SetupManual([]);
        SetupWebhook([]);
        SetupRetry([]);

        var result = await _handler.HandleAsync(
            new PollIntegrationsCommand(_tenantId, "production", _leaseOwnerId));

        Assert.Empty(result.Integrations);
    }

    [Fact]
    public async Task HandleAsync_ReturnsClaimedManualRuns()
    {
        var integrationId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();
        var manualRunId = Guid.NewGuid();
        var claimExpiresAt = DateTime.UtcNow.AddMinutes(5);

        var integration = MakeIntegration(integrationId);
        var workItem = MakeWorkItem(workItemId, integrationId, TriggerSource.Manual, claimExpiresAt, manualRunId);

        SetupScheduled([]);
        SetupManual([new ClaimedWork(integration, workItem)]);
        SetupWebhook([]);
        SetupRetry([]);

        var result = await _handler.HandleAsync(
            new PollIntegrationsCommand(_tenantId, "production", _leaseOwnerId));

        var item = Assert.Single(result.Integrations);
        Assert.Equal(integrationId, item.Id);
        Assert.Equal(TriggerSource.Manual, item.TriggerSource);
        Assert.Equal(manualRunId, item.ManualRunRequestId);
        Assert.Equal(claimExpiresAt, item.LeaseExpiresAt);
        Assert.Equal(workItemId, item.WorkItemId);
    }

    [Fact]
    public async Task HandleAsync_CombinesBothScheduledAndManualRuns()
    {
        var scheduledId = Guid.NewGuid();
        var manualId = Guid.NewGuid();
        var expires = DateTime.UtcNow.AddMinutes(5);

        SetupScheduled([new ClaimedWork(
            MakeIntegration(scheduledId, "Scheduled Job"),
            MakeWorkItem(Guid.NewGuid(), scheduledId, TriggerSource.Scheduled, expires))]);

        SetupManual([new ClaimedWork(
            MakeIntegration(manualId, "Manual Job"),
            MakeWorkItem(Guid.NewGuid(), manualId, TriggerSource.Manual, expires, Guid.NewGuid()))]);
        SetupWebhook([]);
        SetupRetry([]);

        var result = await _handler.HandleAsync(
            new PollIntegrationsCommand(_tenantId, "production", _leaseOwnerId));

        Assert.Equal(2, result.Integrations.Count);
        Assert.Contains(result.Integrations, i => i.TriggerSource == TriggerSource.Scheduled);
        Assert.Contains(result.Integrations, i => i.TriggerSource == TriggerSource.Manual);
    }

    [Fact]
    public async Task HandleAsync_ReturnsClaimedWebhookRunsWithPayload()
    {
        var integrationId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();
        var claimExpiresAt = DateTime.UtcNow.AddMinutes(5);

        var integration = MakeIntegration(integrationId);
        integration.TriggerType = TriggerType.Webhook;
        integration.CronExpression = null;

        var workItem = MakeWorkItem(workItemId, integrationId, TriggerSource.Webhook, claimExpiresAt);
        workItem.Payload = """{"event":"created"}""";

        SetupScheduled([]);
        SetupManual([]);
        SetupWebhook([new ClaimedWork(integration, workItem)]);
        SetupRetry([]);

        var result = await _handler.HandleAsync(
            new PollIntegrationsCommand(_tenantId, "production", _leaseOwnerId));

        var item = Assert.Single(result.Integrations);
        Assert.Equal(integrationId, item.Id);
        Assert.Equal(TriggerSource.Webhook, item.TriggerSource);
        Assert.Null(item.ManualRunRequestId);
        Assert.Equal(workItemId, item.WorkItemId);
        Assert.Equal("""{"event":"created"}""", item.Payload);
    }

    [Fact]
    public async Task HandleAsync_ReturnsClaimedRetryRuns()
    {
        var integrationId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();
        var claimExpiresAt = DateTime.UtcNow.AddMinutes(5);

        var integration = MakeIntegration(integrationId);
        var workItem = MakeWorkItem(workItemId, integrationId, TriggerSource.Retry, claimExpiresAt);

        SetupScheduled([]);
        SetupManual([]);
        SetupWebhook([]);
        SetupRetry([new ClaimedWork(integration, workItem)]);

        var result = await _handler.HandleAsync(
            new PollIntegrationsCommand(_tenantId, "production", _leaseOwnerId));

        var item = Assert.Single(result.Integrations);
        Assert.Equal(integrationId, item.Id);
        Assert.Equal(TriggerSource.Retry, item.TriggerSource);
        Assert.Equal(workItemId, item.WorkItemId);
    }

    private void SetupScheduled(IReadOnlyList<ClaimedWork> result) =>
        _repository.ClaimDueScheduledAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Any<TimeSpan>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
        .Returns(result);

    private void SetupManual(IReadOnlyList<ClaimedWork> result) =>
        _repository.ClaimPendingManualRunsAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Any<TimeSpan>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
        .Returns(result);

    private void SetupWebhook(IReadOnlyList<ClaimedWork> result) =>
        _repository.ClaimPendingWebhookRunsAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Any<TimeSpan>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
        .Returns(result);

    private void SetupRetry(IReadOnlyList<ClaimedWork> result) =>
        _repository.ClaimPendingRetryRunsAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Any<TimeSpan>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
        .Returns(result);

    private Integration MakeIntegration(Guid id, string name = "Sync Orders") => new()
    {
        Id = id,
        TenantId = _tenantId,
        Name = name,
        Slug = name.ToLower().Replace(' ', '-'),
        Environment = "production",
        TriggerType = TriggerType.Scheduled,
        CronExpression = "0 * * * *",
        ClassName = "MyCompany.Integrations.SyncOrdersIntegration"
    };

    private static WorkItem MakeWorkItem(
        Guid id, Guid integrationId, TriggerSource source,
        DateTime claimExpiresAt, Guid? manualRunRequestId = null) => new()
    {
        Id = id,
        IntegrationId = integrationId,
        TriggerSource = source,
        Status = WorkItemStatus.Claimed,
        ClaimOwner = Guid.NewGuid(),
        ClaimExpiresAt = claimExpiresAt,
        ManualRunRequestId = manualRunRequestId,
        AvailableAt = DateTime.UtcNow
    };
}
