using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

// Called by the agent to open an execution record before running an integration.
// Validates that the agent holds an active claim on the work item.
public record StartExecutionCommand(
    Guid TenantId,
    string Environment,
    Guid WorkItemId,
    Guid AgentTokenId) : ICommand<StartExecutionResult>;

public record StartExecutionResult(Guid ExecutionId, DateTime StartedAt);

// Called by the agent to close the execution record with the outcome
public record CompleteExecutionCommand(
    Guid TenantId,
    Guid ExecutionId,
    bool Succeeded,
    string? ErrorMessage,
    bool IsTimeout = false) : ICommand<bool>;

public interface IExecutionRepository
{
    Task<ExecutionRecord> CreateAsync(ExecutionRecord record, CancellationToken ct = default);
    Task<ExecutionRecord?> FindAsync(Guid tenantId, Guid executionId, CancellationToken ct = default);
    Task UpdateAsync(ExecutionRecord record, CancellationToken ct = default);
    Task<bool> HasRunningExecutionAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default);
}

public interface IIntegrationValidationRepository
{
    Task<Integration?> GetByIdAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default);
}

public interface IManualRunRequestRepository
{
    Task<ManualRunRequest?> GetByIdAsync(Guid tenantId, Guid requestId, CancellationToken ct = default);
    Task MarkStartedAsync(Guid requestId, Guid executionRecordId, CancellationToken ct = default);
}

public class StartExecutionHandler(
    IExecutionRepository repository,
    IWorkItemRepository workItemRepository,
    IIntegrationValidationRepository integrationRepository,
    IManualRunRequestRepository manualRunRepository)
    : ICommandHandler<StartExecutionCommand, StartExecutionResult>
{
    public async Task<StartExecutionResult> HandleAsync(StartExecutionCommand command, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var workItem = await workItemRepository.GetByIdAsync(command.TenantId, command.WorkItemId, ct);

        if (workItem is null)
            throw new NotFoundException($"Work item '{command.WorkItemId}' not found.");

        if (!workItem.IsClaimOwnedBy(command.AgentTokenId, now))
        {
            if (workItem.HasActiveClaim(now))
                throw new ConflictException($"Work item '{command.WorkItemId}' is claimed by a different agent.");
            else
                throw new ValidationException($"Work item claim has expired. Re-poll to reclaim.");
        }

        if (workItem.Status != WorkItemStatus.Claimed)
            throw new ValidationException($"Work item '{command.WorkItemId}' is not in Claimed status (current: {workItem.Status}).");

        var integration = await integrationRepository.GetByIdAsync(command.TenantId, workItem.IntegrationId, ct);

        if (integration is null)
            throw new NotFoundException($"Integration '{workItem.IntegrationId}' not found.");

        if (integration.Environment != command.Environment)
            throw new ValidationException($"Integration belongs to environment '{integration.Environment}', not '{command.Environment}'.");

        if (integration.Status != IntegrationStatus.Enabled)
            throw new ValidationException($"Integration '{integration.Id}' is disabled.");

        // Prevent overlap: reject if another execution is already running
        if (await repository.HasRunningExecutionAsync(command.TenantId, workItem.IntegrationId, ct))
            throw new ConflictException($"Integration '{integration.Id}' already has a running execution.");

        var record = new ExecutionRecord
        {
            TenantId = command.TenantId,
            IntegrationId = workItem.IntegrationId,
            Environment = command.Environment,
            Status = ExecutionStatus.Running,
            TriggerSource = workItem.TriggerSource,
            StartedAt = now,
            WorkItemId = command.WorkItemId
        };

        var created = await repository.CreateAsync(record, ct);

        // Mark the work item as started
        workItem.Status = WorkItemStatus.Started;
        workItem.UpdatedAt = now;
        await workItemRepository.UpdateAsync(workItem, ct);

        // For manual runs, mark the ManualRunRequest as started
        if (workItem.TriggerSource == TriggerSource.Manual && workItem.ManualRunRequestId.HasValue)
            await manualRunRepository.MarkStartedAsync(workItem.ManualRunRequestId.Value, created.Id, ct);

        return new StartExecutionResult(created.Id, created.StartedAt);
    }
}

public class CompleteExecutionHandler(
    IExecutionRepository repository,
    IWorkItemRepository workItemRepository)
    : ICommandHandler<CompleteExecutionCommand, bool>
{
    public async Task<bool> HandleAsync(CompleteExecutionCommand command, CancellationToken ct = default)
    {
        var record = await repository.FindAsync(command.TenantId, command.ExecutionId, ct);

        if (record is null)
            throw new NotFoundException("Execution record not found.");

        record.Status = command.Succeeded
            ? ExecutionStatus.Succeeded
            : command.IsTimeout
                ? ExecutionStatus.TimedOut
                : ExecutionStatus.Failed;
        record.CompletedAt = DateTime.UtcNow;
        record.ErrorMessage = command.ErrorMessage;
        record.UpdatedAt = DateTime.UtcNow;

        await repository.UpdateAsync(record, ct);

        // Mirror terminal status onto the work item
        if (record.WorkItemId.HasValue)
        {
            var workItem = await workItemRepository.GetByIdAsync(command.TenantId, record.WorkItemId.Value, ct);
            if (workItem is not null)
            {
                workItem.Status = command.Succeeded
                    ? WorkItemStatus.Completed
                    : command.IsTimeout
                        ? WorkItemStatus.TimedOut
                        : WorkItemStatus.Failed;
                workItem.UpdatedAt = DateTime.UtcNow;
                await workItemRepository.UpdateAsync(workItem, ct);
            }
        }

        return true;
    }
}
