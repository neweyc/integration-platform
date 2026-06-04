using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Cronos;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

public record UpdateIntegrationCommand(
    Guid TenantId,
    Guid IntegrationId,
    string Name,
    string? Description,
    IntegrationStatus Status,
    string? CronExpression,
    int? TimeoutSeconds = null,
    int RetryMaxAttempts = 0,
    int? RetryBackoffSeconds = null,
    Guid? PackageId = null) : ICommand<CreateIntegrationResult>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        new(AuditAction.IntegrationUpdated, "Integration", IntegrationId.ToString(), $"Updated integration '{Name}'");
}

public interface IIntegrationUpdateRepository
{
    Task<Integration?> GetByIdAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default);
    Task<bool> PackageExistsAsync(Guid tenantId, Guid packageId, CancellationToken ct = default);
    Task<Integration> UpdateAsync(Integration integration, CancellationToken ct = default);
}

public class UpdateIntegrationHandler(IIntegrationUpdateRepository repository)
    : ICommandHandler<UpdateIntegrationCommand, CreateIntegrationResult>
{
    public async Task<CreateIntegrationResult> HandleAsync(UpdateIntegrationCommand command, CancellationToken ct = default)
    {
        var integration = await repository.GetByIdAsync(command.TenantId, command.IntegrationId, ct);

        if (integration is null)
            throw new NotFoundException($"Integration '{command.IntegrationId}' not found.");

        ValidateCommand(command, integration.TriggerType);

        if (command.PackageId.HasValue
            && !await repository.PackageExistsAsync(command.TenantId, command.PackageId.Value, ct))
            throw new NotFoundException($"Package '{command.PackageId}' not found.");

        integration.Name = command.Name;
        integration.Description = command.Description;
        integration.Status = command.Status;
        integration.CronExpression = command.CronExpression;
        integration.TimeoutSeconds = command.TimeoutSeconds;
        integration.RetryMaxAttempts = command.RetryMaxAttempts;
        integration.RetryBackoffSeconds = command.RetryBackoffSeconds;
        integration.PackageId = command.PackageId;
        integration.UpdatedAt = DateTime.UtcNow;

        var updated = await repository.UpdateAsync(integration, ct);

        return CreateIntegrationHandler.ToResult(updated);
    }

    private static void ValidateCommand(UpdateIntegrationCommand command, TriggerType triggerType)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ValidationException("Name is required.");

        if (command.TimeoutSeconds is <= 0)
            throw new ValidationException("Timeout must be greater than zero seconds.");

        if (command.RetryMaxAttempts < 0)
            throw new ValidationException("Retry max attempts cannot be negative.");

        if (command.RetryBackoffSeconds is < 0)
            throw new ValidationException("Retry backoff cannot be negative.");

        // Cron expression is only relevant for scheduled integrations
        if (triggerType == TriggerType.Scheduled)
        {
            if (string.IsNullOrWhiteSpace(command.CronExpression))
                throw new ValidationException("A cron expression is required for scheduled integrations.");

            try
            {
                CronExpression.Parse(command.CronExpression);
            }
            catch
            {
                throw new ValidationException($"'{command.CronExpression}' is not a valid cron expression.");
            }
        }
    }
}
