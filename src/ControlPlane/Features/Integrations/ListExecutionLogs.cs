using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

public record ListExecutionLogsCommand(
    Guid TenantId,
    Guid IntegrationId,
    Guid ExecutionId) : ICommand<ListExecutionLogsResult>;

public record ListExecutionLogsResult(IReadOnlyList<ExecutionLogItem> Logs);

public record ExecutionLogItem(
    Guid Id,
    DateTime Timestamp,
    string Level,
    string Message,
    string? Exception,
    string? PropertiesJson);

public interface IExecutionLogReadRepository
{
    Task<bool> ExecutionBelongsToIntegrationAsync(
        Guid tenantId,
        Guid integrationId,
        Guid executionId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ExecutionLog>> ListForExecutionAsync(
        Guid tenantId,
        Guid executionId,
        CancellationToken ct = default);
}

public class ListExecutionLogsHandler(IExecutionLogReadRepository repository)
    : ICommandHandler<ListExecutionLogsCommand, ListExecutionLogsResult>
{
    public async Task<ListExecutionLogsResult> HandleAsync(
        ListExecutionLogsCommand command,
        CancellationToken ct = default)
    {
        var executionExists = await repository.ExecutionBelongsToIntegrationAsync(
            command.TenantId,
            command.IntegrationId,
            command.ExecutionId,
            ct);

        if (!executionExists)
            throw new NotFoundException("Execution record not found.");

        var logs = await repository.ListForExecutionAsync(command.TenantId, command.ExecutionId, ct);
        return new ListExecutionLogsResult(logs.Select(ToItem).ToList());
    }

    private static ExecutionLogItem ToItem(ExecutionLog log)
    {
        return new ExecutionLogItem(
            log.Id,
            log.Timestamp,
            log.Level,
            log.Message,
            log.Exception,
            log.PropertiesJson);
    }
}
