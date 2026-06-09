using ControlPlane.Features.AgentTokens;
using ControlPlane.Features.Alerts;
using ControlPlane.Features.Triggers;
using ControlPlane.Features.Workflows;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.AgentTokens;

public class RetryPolicyTests
{
    private readonly IExecutionRepository _executionRepository = Substitute.For<IExecutionRepository>();
    private readonly IWorkItemRepository _workItemRepository = Substitute.For<IWorkItemRepository>();
    private readonly IIntegrationValidationRepository _integrationRepository = Substitute.For<IIntegrationValidationRepository>();
    private readonly IWorkflowProgressionService _workflowProgression = Substitute.For<IWorkflowProgressionService>();
    private readonly ITriggerEventRecorder _triggerEvents = Substitute.For<ITriggerEventRecorder>();
    private readonly IAlertDispatchQueue _alertQueue = Substitute.For<IAlertDispatchQueue>();
    private readonly CompleteExecutionHandler _handler;

    public RetryPolicyTests()
    {
        _handler = new CompleteExecutionHandler(_executionRepository, _workItemRepository, _integrationRepository, _workflowProgression, _triggerEvents, _alertQueue);
    }

    [Fact]
    public async Task CompleteExecution_FailedRetryableAttempt_CreatesRetryWorkItem()
    {
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var agentTokenId = Guid.NewGuid();

        var record = new ExecutionRecord
        {
            Id = executionId,
            TenantId = tenantId,
            IntegrationId = integrationId,
            WorkItemId = workItemId,
            Environment = "production",
            TriggerSource = TriggerSource.Scheduled,
            Status = ExecutionStatus.Running,
            AttemptNumber = 1
        };

        _executionRepository.FindAsync(tenantId, executionId).Returns(record);
        _integrationRepository.GetByIdAsync(tenantId, integrationId).Returns(new Integration
        {
            Id = integrationId,
            TenantId = tenantId,
            Environment = "production",
            RetryMaxAttempts = 2,
            RetryBackoffSeconds = 30
        });
        _workItemRepository.GetByIdAsync(tenantId, workItemId).Returns(new WorkItem
        {
            Id = workItemId,
            TenantId = tenantId,
            IntegrationId = integrationId,
            Environment = "production",
            TriggerSource = TriggerSource.Scheduled,
            Payload = "payload",
            Status = WorkItemStatus.Started,
            ClaimOwner = agentTokenId
        });

        await _handler.HandleAsync(new CompleteExecutionCommand(
            tenantId, "production", executionId, agentTokenId, Succeeded: false, ErrorMessage: "failed"));

        await _workItemRepository.Received(1).CreateAsync(Arg.Is<WorkItem>(w =>
            w.TenantId == tenantId
            && w.IntegrationId == integrationId
            && w.Environment == "production"
            && w.TriggerSource == TriggerSource.Retry
            && w.Status == WorkItemStatus.Pending
            && w.AttemptNumber == 2
            && w.ParentExecutionId == executionId
            && w.RootExecutionId == executionId
            && w.Payload == "payload"));

        await _triggerEvents.Received(1).RecordAsync(Arg.Is<TriggerEventRecord>(e =>
            e.TenantId == tenantId
            && e.IntegrationId == integrationId
            && e.AdapterKey == "retry"
            && e.Source == TriggerSource.Retry
            && e.Outcome == TriggerEventOutcome.ConvertedToWork
            && e.EventKey == executionId.ToString()
            && e.WorkItemId.HasValue
            && e.MetadataJson != null
            && e.MetadataJson.Contains("\"attemptNumber\":2")));

        // A retry is queued, so this failure is not terminal — no alert should fire yet.
        _alertQueue.DidNotReceive().Enqueue(Arg.Any<FailedExecutionAlert>());
    }

    [Fact]
    public async Task CompleteExecution_TerminalFailure_EnqueuesAlert()
    {
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var agentTokenId = Guid.NewGuid();

        _executionRepository.FindAsync(tenantId, executionId).Returns(new ExecutionRecord
        {
            Id = executionId,
            TenantId = tenantId,
            IntegrationId = integrationId,
            Environment = "production",
            Status = ExecutionStatus.Running,
            AttemptNumber = 1
        });
        _integrationRepository.GetByIdAsync(tenantId, integrationId).Returns(new Integration
        {
            Id = integrationId,
            TenantId = tenantId,
            Name = "Sync Orders",
            RetryMaxAttempts = 0 // no retries — this failure is terminal
        });

        await _handler.HandleAsync(new CompleteExecutionCommand(
            tenantId, "production", executionId, agentTokenId, Succeeded: false, ErrorMessage: "boom"));

        _alertQueue.Received(1).Enqueue(Arg.Is<FailedExecutionAlert>(a =>
            a.TenantId == tenantId
            && a.IntegrationId == integrationId
            && a.IntegrationName == "Sync Orders"
            && a.Environment == "production"
            && a.ExecutionId == executionId
            && a.Status == ExecutionStatus.Failed
            && a.ErrorMessage == "boom"));
    }

    [Fact]
    public async Task CompleteExecution_Success_DoesNotEnqueueAlert()
    {
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var agentTokenId = Guid.NewGuid();

        _executionRepository.FindAsync(tenantId, executionId).Returns(new ExecutionRecord
        {
            Id = executionId,
            TenantId = tenantId,
            IntegrationId = integrationId,
            Environment = "production",
            Status = ExecutionStatus.Running,
            AttemptNumber = 1
        });
        _integrationRepository.GetByIdAsync(tenantId, integrationId).Returns(new Integration
        {
            Id = integrationId,
            TenantId = tenantId
        });

        await _handler.HandleAsync(new CompleteExecutionCommand(
            tenantId, "production", executionId, agentTokenId, Succeeded: true, ErrorMessage: null));

        _alertQueue.DidNotReceive().Enqueue(Arg.Any<FailedExecutionAlert>());
    }

    [Fact]
    public async Task CompleteExecution_LastAttempt_DoesNotCreateRetry()
    {
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var agentTokenId = Guid.NewGuid();

        _executionRepository.FindAsync(tenantId, executionId).Returns(new ExecutionRecord
        {
            Id = executionId,
            TenantId = tenantId,
            IntegrationId = integrationId,
            Environment = "production",
            Status = ExecutionStatus.Running,
            AttemptNumber = 3
        });
        _integrationRepository.GetByIdAsync(tenantId, integrationId).Returns(new Integration
        {
            Id = integrationId,
            TenantId = tenantId,
            RetryMaxAttempts = 2
        });

        await _handler.HandleAsync(new CompleteExecutionCommand(
            tenantId, "production", executionId, agentTokenId, Succeeded: false, ErrorMessage: "failed"));

        await _workItemRepository.DidNotReceive().CreateAsync(Arg.Any<WorkItem>());
    }

    [Fact]
    public async Task CompleteExecution_NonRetryableFailure_DoesNotCreateRetry()
    {
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var agentTokenId = Guid.NewGuid();

        _executionRepository.FindAsync(tenantId, executionId).Returns(new ExecutionRecord
        {
            Id = executionId,
            TenantId = tenantId,
            IntegrationId = integrationId,
            Environment = "production",
            Status = ExecutionStatus.Running,
            AttemptNumber = 1
        });
        _integrationRepository.GetByIdAsync(tenantId, integrationId).Returns(new Integration
        {
            Id = integrationId,
            TenantId = tenantId,
            RetryMaxAttempts = 2
        });

        await _handler.HandleAsync(new CompleteExecutionCommand(
            tenantId, "production", executionId, agentTokenId, Succeeded: false, ErrorMessage: "shutdown", Retryable: false));

        await _workItemRepository.DidNotReceive().CreateAsync(Arg.Any<WorkItem>());
    }
}
