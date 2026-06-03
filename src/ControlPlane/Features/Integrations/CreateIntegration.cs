using ControlPlane.Infrastructure;
using Cronos;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

public record CreateIntegrationCommand(
    Guid TenantId,
    string Name,
    string Slug,
    string? Description,
    string Environment,
    TriggerType TriggerType,
    string? CronExpression,
    string ClassName,
    int? TimeoutSeconds = null,
    Guid? PackageId = null) : ICommand<CreateIntegrationResult>;

public record CreateIntegrationResult(
    Guid Id,
    string Name,
    string Slug,
    string Environment,
    string Status,
    string TriggerType,
    string? CronExpression,
    string ClassName,
    int? TimeoutSeconds = null,
    Guid? PackageId = null);

public interface IIntegrationRepository
{
    Task<bool> SlugExistsAsync(Guid tenantId, string slug, CancellationToken ct = default);
    Task<bool> PackageExistsAsync(Guid tenantId, Guid packageId, CancellationToken ct = default);
    Task<Integration> CreateAsync(Integration integration, CancellationToken ct = default);
}

public class CreateIntegrationHandler(IIntegrationRepository repository)
    : ICommandHandler<CreateIntegrationCommand, CreateIntegrationResult>
{
    public async Task<CreateIntegrationResult> HandleAsync(CreateIntegrationCommand command, CancellationToken ct = default)
    {
        ValidateCommand(command);

        if (await repository.SlugExistsAsync(command.TenantId, command.Slug, ct))
            throw new ConflictException($"An integration with slug '{command.Slug}' already exists.");

        if (command.PackageId.HasValue
            && !await repository.PackageExistsAsync(command.TenantId, command.PackageId.Value, ct))
            throw new NotFoundException($"Package '{command.PackageId}' not found.");

        var integration = new Integration
        {
            TenantId = command.TenantId,
            Name = command.Name,
            Slug = command.Slug,
            Description = command.Description,
            Environment = command.Environment,
            TriggerType = command.TriggerType,
            CronExpression = command.CronExpression,
            ClassName = command.ClassName,
            TimeoutSeconds = command.TimeoutSeconds,
            PackageId = command.PackageId,
            Status = IntegrationStatus.Enabled
        };

        var created = await repository.CreateAsync(integration, ct);

        return ToResult(created);
    }

    private static void ValidateCommand(CreateIntegrationCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ValidationException("Name is required.");

        if (string.IsNullOrWhiteSpace(command.Slug))
            throw new ValidationException("Slug is required.");

        if (!System.Text.RegularExpressions.Regex.IsMatch(command.Slug, @"^[a-z0-9-]+$"))
            throw new ValidationException("Slug may only contain lowercase letters, numbers, and hyphens.");

        if (string.IsNullOrWhiteSpace(command.Environment))
            throw new ValidationException("Environment is required.");

        if (string.IsNullOrWhiteSpace(command.ClassName))
            throw new ValidationException("Class name is required.");

        // Class name should look like a valid fully-qualified .NET type name
        if (!System.Text.RegularExpressions.Regex.IsMatch(command.ClassName, @"^[\w]+(?:\.[\w]+)*$"))
            throw new ValidationException("Class name must be a valid fully-qualified .NET type name (e.g. 'MyCompany.Integrations.SyncOrdersIntegration').");

        if (command.TimeoutSeconds is <= 0)
            throw new ValidationException("Timeout must be greater than zero seconds.");

        if (command.TriggerType == TriggerType.Scheduled)
        {
            if (string.IsNullOrWhiteSpace(command.CronExpression))
                throw new ValidationException("A cron expression is required for scheduled integrations.");

            if (!IsValidCronExpression(command.CronExpression))
                throw new ValidationException($"'{command.CronExpression}' is not a valid cron expression.");
        }
    }

    private static bool IsValidCronExpression(string expression)
    {
        try
        {
            CronExpression.Parse(expression);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static CreateIntegrationResult ToResult(Integration i) =>
        new(i.Id, i.Name, i.Slug, i.Environment, i.Status.ToString(), i.TriggerType.ToString(),
            i.CronExpression, i.ClassName, i.TimeoutSeconds, i.PackageId);
}
