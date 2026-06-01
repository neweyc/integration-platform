using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

// Called by the agent to open an execution record before running an integration.
// For scheduled integrations, validates that the requesting agent holds the lease.
public record StartExecutionCommand(
    Guid TenantId,
    string Environment,
    Guid IntegrationId,
    Guid AgentTokenId) : ICommand<StartExecutionResult>;

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

public class StartExecutionHandler(
    IExecutionRepository repository,
    IIntegrationValidationRepository integrationRepository,
    IScheduleStateRepository scheduleStateRepository)
    : ICommandHandler<StartExecutionCommand, StartExecutionResult>
{
    public async Task<StartExecutionResult> HandleAsync(StartExecutionCommand command, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Validate that the integration exists, belongs to the tenant, matches the environment, and is enabled
        var integration = await integrationRepository.GetByIdAsync(command.TenantId, command.IntegrationId, ct);

        if (integration is null)
            throw new NotFoundException($"Integration '{command.IntegrationId}' not found.");

        if (integration.Environment != command.Environment)
            throw new ValidationException($"Integration '{command.IntegrationId}' belongs to environment '{integration.Environment}', not '{command.Environment}'.");

        if (integration.Status != IntegrationStatus.Enabled)
            throw new ValidationException($"Integration '{command.IntegrationId}' is disabled.");

        // For scheduled integrations, validate lease ownership
        if (integration.TriggerType == TriggerType.Scheduled)
        {
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
            StartedAt = now,
        };

        var created = await repository.CreateAsync(record, ct);
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

        // Clear the lease now that execution is complete
        await scheduleStateRepository.ClearLeaseAsync(command.TenantId, record.IntegrationId, ct);

        return true;
    }
}
