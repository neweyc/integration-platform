using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

public record ListIntegrationExecutionsCommand(
    Guid TenantId,
    Guid IntegrationId,
    int Limit) : ICommand<ListIntegrationExecutionsResult>;

public record ListIntegrationExecutionsResult(IReadOnlyList<ExecutionSummary> Executions);

public record ExecutionSummary(
    Guid Id,
    string Status,
    string Environment,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int? DurationMs,
    string? ErrorMessage,
    string? PackageName = null,
    string? PackageVersion = null);

public interface IExecutionHistoryRepository
{
    Task<ExecutionRecord?> GetLatestForIntegrationAsync(
        Guid tenantId,
        Guid integrationId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ExecutionRecord>> ListForIntegrationAsync(
        Guid tenantId,
        Guid integrationId,
        int limit,
        CancellationToken ct = default);

    Task<Dictionary<Guid, ExecutionRecord>> GetLatestForIntegrationsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> integrationIds,
        CancellationToken ct = default);
}

public class ListIntegrationExecutionsHandler(
    IIntegrationReadRepository integrationRepository,
    IExecutionHistoryRepository executionRepository)
    : ICommandHandler<ListIntegrationExecutionsCommand, ListIntegrationExecutionsResult>
{
    private const int MaxLimit = 100;

    public async Task<ListIntegrationExecutionsResult> HandleAsync(
        ListIntegrationExecutionsCommand command,
        CancellationToken ct = default)
    {
        var integration = await integrationRepository.GetByIdAsync(command.TenantId, command.IntegrationId, ct);

        if (integration is null)
            throw new NotFoundException($"Integration '{command.IntegrationId}' not found.");

        var limit = Math.Clamp(command.Limit, 1, MaxLimit);
        var executions = await executionRepository.ListForIntegrationAsync(
            command.TenantId,
            command.IntegrationId,
            limit,
            ct);

        return new ListIntegrationExecutionsResult(executions.Select(ToSummary).ToList());
    }

    internal static ExecutionSummary ToSummary(ExecutionRecord execution)
    {
        var duration = execution.CompletedAt is null
            ? null
            : (int?)(execution.CompletedAt.Value - execution.StartedAt).TotalMilliseconds;

        return new ExecutionSummary(
            execution.Id,
            execution.Status.ToString(),
            execution.Environment,
            execution.StartedAt,
            execution.CompletedAt,
            duration,
            execution.ErrorMessage,
            execution.PackageName,
            execution.PackageVersion);
    }
}
