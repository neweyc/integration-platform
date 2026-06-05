using ControlPlane.Infrastructure;
using ControlPlane.Features.Triggers;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

/// <summary>
/// Requests a manual run of an integration. Creates a pending ManualRunRequest
/// that agents will pick up on their next poll.
/// </summary>
public record RequestManualRunCommand(
    Guid TenantId,
    Guid IntegrationId) : ICommand<ManualRunResult>;

public record ManualRunResult(
    Guid RequestId,
    Guid IntegrationId,
    string IntegrationName,
    string Environment,
    DateTime RequestedAt);

public interface IManualRunRepository
{
    Task<Integration?> GetIntegrationAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default);
    Task<bool> HasPendingRunAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default);
    Task<bool> HasRunningExecutionAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default);
    Task<ManualRunRequest> CreateAsync(ManualRunRequest request, CancellationToken ct = default);
}

public class RequestManualRunHandler(IManualRunRepository repository, ITriggerWorkItemProducer workItemProducer)
    : ICommandHandler<RequestManualRunCommand, ManualRunResult>
{
    public async Task<ManualRunResult> HandleAsync(RequestManualRunCommand command, CancellationToken ct = default)
    {
        var integration = await repository.GetIntegrationAsync(command.TenantId, command.IntegrationId, ct);

        if (integration is null)
            throw new NotFoundException($"Integration '{command.IntegrationId}' not found.");

        if (integration.Status != IntegrationStatus.Enabled)
            throw new ValidationException($"Cannot run disabled integration '{integration.Name}'.");

        // Check for existing pending manual run
        if (await repository.HasPendingRunAsync(command.TenantId, command.IntegrationId, ct))
            throw new ConflictException($"A manual run is already pending for '{integration.Name}'.");

        // Check for running execution (prevents overlap)
        if (await repository.HasRunningExecutionAsync(command.TenantId, command.IntegrationId, ct))
            throw new ConflictException($"Integration '{integration.Name}' is already running.");

        var now = DateTime.UtcNow;
        var request = new ManualRunRequest
        {
            TenantId = command.TenantId,
            IntegrationId = command.IntegrationId,
            Environment = integration.Environment,
            Status = ManualRunStatus.Pending,
            RequestedAt = now
        };

        var created = await repository.CreateAsync(request, ct);

        await workItemProducer.EnqueueAsync(
            new TriggerWorkItemRequest(
                command.TenantId,
                command.IntegrationId,
                integration.Environment,
                TriggerSource.Manual,
                now,
                ManualRunRequestId: created.Id),
            ct);

        return new ManualRunResult(
            created.Id,
            integration.Id,
            integration.Name,
            integration.Environment,
            created.RequestedAt);
    }
}

public class ManualRunRepository(AppDbContext db) : IManualRunRepository
{
    public async Task<Integration?> GetIntegrationAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default)
    {
        return await db.Integrations
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == integrationId, ct);
    }

    public async Task<bool> HasPendingRunAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default)
    {
        return await db.WorkItems
            .AnyAsync(w => w.TenantId == tenantId
                        && w.IntegrationId == integrationId
                        && w.TriggerSource == TriggerSource.Manual
                        && (w.Status == WorkItemStatus.Pending
                            || w.Status == WorkItemStatus.Claimed
                            || w.Status == WorkItemStatus.Started),
                ct);
    }

    public async Task<bool> HasRunningExecutionAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default)
    {
        return await db.ExecutionRecords
            .AnyAsync(e => e.TenantId == tenantId
                        && e.IntegrationId == integrationId
                        && e.Status == ExecutionStatus.Running,
                ct);
    }

    public async Task<ManualRunRequest> CreateAsync(ManualRunRequest request, CancellationToken ct = default)
    {
        db.ManualRunRequests.Add(request);
        await db.SaveChangesAsync(ct);
        return request;
    }

}
