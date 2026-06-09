using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;
using Environment = Shared.Domain.Environment;

namespace ControlPlane.Features.Environments;

public record CreateEnvironmentCommand(
    Guid TenantId,
    string Name,
    string? DisplayName,
    string? Description,
    int SortOrder,
    bool IsDefault) : ICommand<EnvironmentDto>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        new(AuditAction.EnvironmentCreated, "Environment",
            (result as EnvironmentDto)?.Name, $"Created environment '{(result as EnvironmentDto)?.Name}'");
}

public class CreateEnvironmentHandler(IEnvironmentWriteRepository repository)
    : ICommandHandler<CreateEnvironmentCommand, EnvironmentDto>
{
    public async Task<EnvironmentDto> HandleAsync(CreateEnvironmentCommand command, CancellationToken ct = default)
    {
        var name = EnvironmentKey.Normalize(command.Name);

        if (name.Length == 0)
            throw new ValidationException("Environment name is required.");

        if (!EnvironmentKey.IsValid(name))
            throw new ValidationException("Environment name may only contain lowercase letters, numbers, and hyphens.");

        if (await repository.ExistsAsync(command.TenantId, name, ct))
            throw new ConflictException($"Environment '{name}' already exists.");

        var environment = new Environment
        {
            TenantId = command.TenantId,
            Name = name,
            DisplayName = string.IsNullOrWhiteSpace(command.DisplayName) ? name : command.DisplayName.Trim(),
            Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),
            SortOrder = command.SortOrder,
            IsDefault = command.IsDefault
        };

        await repository.AddAsync(environment, ct);

        // At most one default per tenant: making this one the default clears the flag on the others.
        if (command.IsDefault)
            await ClearOtherDefaults(repository, command.TenantId, name, ct);

        await repository.SaveAsync(ct);

        return EnvironmentDto.From(environment);
    }

    internal static async Task ClearOtherDefaults(
        IEnvironmentWriteRepository repository, Guid tenantId, string keepName, CancellationToken ct)
    {
        var all = await repository.ListTrackedAsync(tenantId, ct);
        foreach (var other in all)
        {
            if (other.IsDefault && other.Name != keepName)
                other.IsDefault = false;
        }
    }
}
