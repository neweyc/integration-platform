using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

// Called by the agent to open an execution record before running an integration.
// For scheduled integrations, validates that the requesting agent holds the lease.
// For manual runs, validates the manual run request exists and was claimed by this agent.
public record StartExecutionCommand(
    Guid TenantId,
    string Environment,
    Guid IntegrationId,
    Guid AgentTokenId,
    string TriggerSource = "Scheduled",
    Guid? ManualRunRequestId = null) : ICommand<StartExecutionResult>;

public record StartExecutionResult(Guid ExecutionId, DateTime StartedAt);

// Called by the agent to close the execution record with the outcome
public record CompleteExecutionCommand(
    Guid TenantId,
    Guid ExecutionId,
    bool Succeeded,
    string? ErrorMessage) : ICommand<bool>;

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

public interface IScheduleStateRepository
{
    Task<IntegrationScheduleState?> GetByIntegrationIdAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default);
    Task ClearLeaseAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default);
}

public interface IManualRunRequestRepository
{
    Task<ManualRunRequest?> GetByIdAsync(Guid tenantId, Guid requestId, CancellationToken ct = default);
    Task MarkStartedAsync(Guid requestId, Guid executionRecordId, CancellationToken ct = default);
}

public class StartExecutionHandler(
    IExecutionRepository repository,
    IIntegrationValidationRepository integrationRepository,
    IScheduleStateRepository scheduleStateRepository,
    IManualRunRequestRepository manualRunRepository)
    : ICommandHandler<StartExecutionCommand, StartExecutionResult>
{
    public async Task<StartExecutionResult> HandleAsync(StartExecutionCommand command, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Parse trigger source
        if (!Enum.TryParse<TriggerSource>(command.TriggerSource, ignoreCase: true, out var triggerSource))
            triggerSource = TriggerSource.Scheduled;

        // Validate that the integration exists, belongs to the tenant, matches the environment, and is enabled
        var integration = await integrationRepository.GetByIdAsync(command.TenantId, command.IntegrationId, ct);

        if (integration is null)
            throw new NotFoundException($"Integration '{command.IntegrationId}' not found.");

        if (integration.Environment != command.Environment)
            throw new ValidationException($"Integration '{command.IntegrationId}' belongs to environment '{integration.Environment}', not '{command.Environment}'.");

        if (integration.Status != IntegrationStatus.Enabled)
            throw new ValidationException($"Integration '{command.IntegrationId}' is disabled.");

        // Check for overlap: prevent starting if another execution is already running
        if (await repository.HasRunningExecutionAsync(command.TenantId, command.IntegrationId, ct))
            throw new ConflictException($"Integration '{command.IntegrationId}' already has a running execution.");

        // Validate based on trigger source
        if (triggerSource == TriggerSource.Manual)
        {
            // For manual runs, validate the manual run request
            if (!command.ManualRunRequestId.HasValue)
                throw new ValidationException("ManualRunRequestId is required for manual trigger source.");

            var manualRequest = await manualRunRepository.GetByIdAsync(command.TenantId, command.ManualRunRequestId.Value, ct);

            if (manualRequest is null)
                throw new NotFoundException($"Manual run request '{command.ManualRunRequestId}' not found.");

            if (manualRequest.IntegrationId != command.IntegrationId)
                throw new ValidationException($"Manual run request does not match integration '{command.IntegrationId}'.");

            if (manualRequest.Status != ManualRunStatus.Claimed)
                throw new ValidationException($"Manual run request is not in Claimed status (current: {manualRequest.Status}).");

            if (manualRequest.ClaimedByAgentTokenId != command.AgentTokenId)
                throw new ConflictException($"Manual run request was claimed by a different agent.");

            // Check claim hasn't expired
            if (manualRequest.IsClaimExpired(now))
                throw new ValidationException($"Manual run claim has expired. Re-poll to reclaim.");
        }
        else if (integration.TriggerType == TriggerType.Scheduled)
        {
            // For scheduled integrations, validate lease ownership
            var state = await scheduleStateRepository.GetByIntegrationIdAsync(command.TenantId, command.IntegrationId, ct);

            if (state is null)
                throw new ValidationException($"No schedule state found for integration '{command.IntegrationId}'. Poll first to claim work.");

            if (!state.IsLeaseOwnedBy(command.AgentTokenId, now))
            {
                if (state.HasActiveLease(now))
                    throw new ConflictException($"Integration '{command.IntegrationId}' is leased by another agent.");
                else
                    throw new ValidationException($"Lease has expired for integration '{command.IntegrationId}'. Re-poll to reclaim.");
            }
        }

        var record = new ExecutionRecord
        {
            TenantId = command.TenantId,
            IntegrationId = command.IntegrationId,
            Environment = command.Environment,
            Status = ExecutionStatus.Running,
            TriggerSource = triggerSource,
            StartedAt = now,
        };

        var created = await repository.CreateAsync(record, ct);

        // For manual runs, mark the request as started with the execution record ID
        if (triggerSource == TriggerSource.Manual && command.ManualRunRequestId.HasValue)
        {
            await manualRunRepository.MarkStartedAsync(command.ManualRunRequestId.Value, created.Id, ct);
        }

        return new StartExecutionResult(created.Id, created.StartedAt);
    }
}

public class CompleteExecutionHandler(IExecutionRepository repository, IScheduleStateRepository scheduleStateRepository)
    : ICommandHandler<CompleteExecutionCommand, bool>
{
    public async Task<bool> HandleAsync(CompleteExecutionCommand command, CancellationToken ct = default)
    {
        var record = await repository.FindAsync(command.TenantId, command.ExecutionId, ct);

        if (record is null)
            throw new NotFoundException("Execution record not found.");

        record.Status = command.Succeeded ? ExecutionStatus.Succeeded : ExecutionStatus.Failed;
        record.CompletedAt = DateTime.UtcNow;
        record.ErrorMessage = command.ErrorMessage;
        record.UpdatedAt = DateTime.UtcNow;

        await repository.UpdateAsync(record, ct);

        // Only clear the schedule lease for scheduled executions
        // Manual runs don't have schedule leases to clear
        if (record.TriggerSource == TriggerSource.Scheduled)
        {
            await scheduleStateRepository.ClearLeaseAsync(command.TenantId, record.IntegrationId, ct);
        }

        return true;
    }
}
