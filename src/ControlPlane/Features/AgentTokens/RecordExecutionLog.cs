using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public record RecordExecutionLogCommand(
    Guid TenantId,
    string Environment,
    Guid ExecutionId,
    DateTime Timestamp,
    string Level,
    string Message,
    string? Exception,
    string? PropertiesJson) : ICommand<bool>;

public interface IExecutionLogRepository
{
    Task<bool> ExecutionExistsAsync(
        Guid tenantId,
        string environment,
        Guid executionId,
        CancellationToken ct = default);

    Task<ExecutionLog> CreateAsync(ExecutionLog log, CancellationToken ct = default);
}

public class RecordExecutionLogHandler(IExecutionLogRepository repository)
    : ICommandHandler<RecordExecutionLogCommand, bool>
{
    public async Task<bool> HandleAsync(RecordExecutionLogCommand command, CancellationToken ct = default)
    {
        Validate(command);

        if (!await repository.ExecutionExistsAsync(command.TenantId, command.Environment, command.ExecutionId, ct))
            throw new NotFoundException("Execution record not found.");

        await repository.CreateAsync(new ExecutionLog
        {
            TenantId = command.TenantId,
            ExecutionRecordId = command.ExecutionId,
            Timestamp = command.Timestamp == default ? DateTime.UtcNow : command.Timestamp,
            Level = command.Level,
            Message = command.Message,
            Exception = command.Exception,
            PropertiesJson = command.PropertiesJson
        }, ct);

        return true;
    }

    private static void Validate(RecordExecutionLogCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Level))
            throw new ValidationException("Log level is required.");

        if (command.Level.Length > 20)
            throw new ValidationException("Log level is too long.");

        if (string.IsNullOrWhiteSpace(command.Message))
            throw new ValidationException("Log message is required.");

        if (command.Message.Length > 4000)
            throw new ValidationException("Log message is too long.");

        if (command.Exception?.Length > 8000)
            throw new ValidationException("Log exception is too long.");
    }
}
