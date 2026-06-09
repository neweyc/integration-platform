using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

public record UpdateIntegrationCommand(
    Guid TenantId,
    Guid IntegrationId,
    string Name,
    string? Description,
    IntegrationStatus Status,
    IReadOnlyList<IntegrationTriggerInput> Triggers,
    int? TimeoutSeconds = null,
    int RetryMaxAttempts = 0,
    int? RetryBackoffSeconds = null) : ICommand<CreateIntegrationResult>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        new(AuditAction.IntegrationUpdated, "Integration", IntegrationId.ToString(), $"Updated integration '{Name}'");
}

public interface IIntegrationUpdateRepository
{
    Task<Integration?> GetByIdAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default);
    Task<bool> PackageExistsAsync(Guid tenantId, Guid packageId, CancellationToken ct = default);
    Task<string?> GetTenantSlugAsync(Guid tenantId, CancellationToken ct = default);
    Task<Integration> UpdateAsync(Integration integration, IReadOnlyList<IntegrationTrigger> triggers, CancellationToken ct = default);
}

public class UpdateIntegrationHandler(IIntegrationUpdateRepository repository, IEncryptionService encryption)
    : ICommandHandler<UpdateIntegrationCommand, CreateIntegrationResult>
{
    public async Task<CreateIntegrationResult> HandleAsync(UpdateIntegrationCommand command, CancellationToken ct = default)
    {
        var integration = await repository.GetByIdAsync(command.TenantId, command.IntegrationId, ct);

        if (integration is null)
            throw new NotFoundException($"Integration '{command.IntegrationId}' not found.");

        ValidateCommand(command);

        integration.Name = command.Name;
        integration.Description = command.Description;
        integration.Status = command.Status;
        integration.TimeoutSeconds = command.TimeoutSeconds;
        integration.RetryMaxAttempts = command.RetryMaxAttempts;
        integration.RetryBackoffSeconds = command.RetryBackoffSeconds;
        // The active package version is intentionally not touched here. It can only be changed through
        // the dedicated repoint endpoint, so a general edit (name, status, triggers) can never alter
        // or un-pin the package.
        integration.UpdatedAt = DateTime.UtcNow;

        var triggers = CreateIntegrationHandler.BuildTriggers(command.TenantId, command.Triggers, encryption);
        var updated = await repository.UpdateAsync(integration, triggers, ct);
        var tenantSlug = await repository.GetTenantSlugAsync(command.TenantId, ct);

        return CreateIntegrationHandler.ToResult(updated, tenantSlug);
    }

    private static void ValidateCommand(UpdateIntegrationCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ValidationException("Name is required.");

        if (command.TimeoutSeconds is <= 0)
            throw new ValidationException("Timeout must be greater than zero seconds.");

        if (command.RetryMaxAttempts < 0)
            throw new ValidationException("Retry max attempts cannot be negative.");

        if (command.RetryBackoffSeconds is < 0)
            throw new ValidationException("Retry backoff cannot be negative.");

        CreateIntegrationHandler.ValidateTriggers(command.Triggers);
    }
}
