using ControlPlane.Features.AgentTokens;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.AgentTokens;

public class ReapOrphanedExecutionsHandlerTests
{
    private static readonly DateTime Now = new(2026, 6, 8, 12, 0, 0, DateTimeKind.Utc);

    private static ExecutionRecord RunningRecord(DateTime startedAt, int? timeoutSeconds, Guid? workItemId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            IntegrationId = Guid.NewGuid(),
            Status = ExecutionStatus.Running,
            StartedAt = startedAt,
            WorkItemId = workItemId,
            Integration = new Integration { TimeoutSeconds = timeoutSeconds }
        };

    [Fact]
    public async Task NoTimeout_RunningPastDefaultCeiling_IsReaped()
    {
        var options = new OrphanedExecutionReaperOptions { DefaultMaxRunningSeconds = 3600 };
        var record = RunningRecord(Now.AddSeconds(-3601), timeoutSeconds: null);
        var repository = Substitute.For<IOrphanedExecutionRepository>();
        repository.ListRunningWithIntegrationAsync().Returns([record]);
        repository.GetWorkItemsAsync(Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(new Dictionary<Guid, WorkItem>());

        var handler = new ReapOrphanedExecutionsHandler(repository, options);
        var result = await handler.HandleAsync(new ReapOrphanedExecutionsCommand(Now));

        Assert.Equal(1, result.ReapedCount);
        Assert.Equal(ExecutionStatus.Failed, record.Status);
        Assert.Equal(Now, record.CompletedAt);
        Assert.Contains("orphaned", record.ErrorMessage);
        await repository.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task NoTimeout_RunningWithinDefaultCeiling_IsNotReaped()
    {
        var options = new OrphanedExecutionReaperOptions { DefaultMaxRunningSeconds = 3600 };
        var record = RunningRecord(Now.AddSeconds(-3599), timeoutSeconds: null);
        var repository = Substitute.For<IOrphanedExecutionRepository>();
        repository.ListRunningWithIntegrationAsync().Returns([record]);

        var handler = new ReapOrphanedExecutionsHandler(repository, options);
        var result = await handler.HandleAsync(new ReapOrphanedExecutionsCommand(Now));

        Assert.Equal(0, result.ReapedCount);
        Assert.Equal(ExecutionStatus.Running, record.Status);
        await repository.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task WithTimeout_RunningWithinTimeoutPlusGrace_IsNotReaped()
    {
        // timeout 300 + grace 120 = 420s ceiling; a 400s run is a legitimately long run, not orphaned.
        var options = new OrphanedExecutionReaperOptions { TimeoutGraceSeconds = 120 };
        var record = RunningRecord(Now.AddSeconds(-400), timeoutSeconds: 300);
        var repository = Substitute.For<IOrphanedExecutionRepository>();
        repository.ListRunningWithIntegrationAsync().Returns([record]);

        var handler = new ReapOrphanedExecutionsHandler(repository, options);
        var result = await handler.HandleAsync(new ReapOrphanedExecutionsCommand(Now));

        Assert.Equal(0, result.ReapedCount);
        Assert.Equal(ExecutionStatus.Running, record.Status);
    }

    [Fact]
    public async Task WithTimeout_RunningPastTimeoutPlusGrace_IsReaped()
    {
        var options = new OrphanedExecutionReaperOptions { TimeoutGraceSeconds = 120 };
        var record = RunningRecord(Now.AddSeconds(-421), timeoutSeconds: 300);
        var repository = Substitute.For<IOrphanedExecutionRepository>();
        repository.ListRunningWithIntegrationAsync().Returns([record]);
        repository.GetWorkItemsAsync(Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(new Dictionary<Guid, WorkItem>());

        var handler = new ReapOrphanedExecutionsHandler(repository, options);
        var result = await handler.HandleAsync(new ReapOrphanedExecutionsCommand(Now));

        Assert.Equal(1, result.ReapedCount);
        Assert.Equal(ExecutionStatus.Failed, record.Status);
    }

    [Fact]
    public async Task Reaping_MirrorsTerminalStatusOntoWorkItem()
    {
        var options = new OrphanedExecutionReaperOptions { DefaultMaxRunningSeconds = 3600 };
        var workItemId = Guid.NewGuid();
        var record = RunningRecord(Now.AddSeconds(-4000), timeoutSeconds: null, workItemId);
        var workItem = new WorkItem { Id = workItemId, Status = WorkItemStatus.Started };

        var repository = Substitute.For<IOrphanedExecutionRepository>();
        repository.ListRunningWithIntegrationAsync().Returns([record]);
        repository.GetWorkItemsAsync(Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(new Dictionary<Guid, WorkItem> { [workItemId] = workItem });

        var handler = new ReapOrphanedExecutionsHandler(repository, options);
        await handler.HandleAsync(new ReapOrphanedExecutionsCommand(Now));

        Assert.Equal(WorkItemStatus.Failed, workItem.Status);
    }

    [Fact]
    public async Task Reaping_DoesNotQueueRetry_OnlyReleasesGuard()
    {
        // The reaper must not create new work items (no retry). It only flips terminal statuses.
        var options = new OrphanedExecutionReaperOptions { DefaultMaxRunningSeconds = 3600 };
        var workItemId = Guid.NewGuid();
        var record = RunningRecord(Now.AddSeconds(-4000), timeoutSeconds: null, workItemId);
        var workItem = new WorkItem { Id = workItemId, Status = WorkItemStatus.Started };

        var repository = Substitute.For<IOrphanedExecutionRepository>();
        repository.ListRunningWithIntegrationAsync().Returns([record]);
        repository.GetWorkItemsAsync(Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(new Dictionary<Guid, WorkItem> { [workItemId] = workItem });

        var handler = new ReapOrphanedExecutionsHandler(repository, options);
        var result = await handler.HandleAsync(new ReapOrphanedExecutionsCommand(Now));

        // Only the orphaned record is reaped; the repository exposes no create path, so by contract
        // no retry work item can be produced here.
        Assert.Equal(1, result.ReapedCount);
    }

    [Fact]
    public async Task NoRunningExecutions_DoesNothing()
    {
        var repository = Substitute.For<IOrphanedExecutionRepository>();
        repository.ListRunningWithIntegrationAsync().Returns([]);

        var handler = new ReapOrphanedExecutionsHandler(repository, new OrphanedExecutionReaperOptions());
        var result = await handler.HandleAsync(new ReapOrphanedExecutionsCommand(Now));

        Assert.Equal(0, result.ReapedCount);
        await repository.DidNotReceive().SaveChangesAsync();
    }
}
