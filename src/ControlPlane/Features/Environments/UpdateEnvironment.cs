using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Features.Environments;

// Name is the immutable key; only presentational fields and the default flag are editable.
public record UpdateEnvironmentCommand(
    Guid TenantId,
    string Name,
    string? DisplayName,
    string? Description,
    int SortOrder,
    bool IsDefault) : ICommand<EnvironmentDto>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        new(AuditAction.EnvironmentUpdated, "Environment",
            EnvironmentKey.Normalize(Name), $"Updated environment '{EnvironmentKey.Normalize(Name)}'");
}

public class UpdateEnvironmentHandler(IEnvironmentWriteRepository repository)
    : ICommandHandler<UpdateEnvironmentCommand, EnvironmentDto>
{
    public async Task<EnvironmentDto> HandleAsync(UpdateEnvironmentCommand command, CancellationToken ct = default)
    {
        var name = EnvironmentKey.Normalize(command.Name);
        var environment = await repository.FindAsync(command.TenantId, name, ct)
            ?? throw new NotFoundException($"Environment '{name}' not found.");

        // A tenant must always have exactly one default. You can't clear the default flag directly —
        // make another environment the default instead, which moves it.
        if (environment.IsDefault && !command.IsDefault)
            throw new ValidationException(
                "Cannot remove the default flag directly. Make another environment the default instead.");

        environment.DisplayName = string.IsNullOrWhiteSpace(command.DisplayName) ? name : command.DisplayName.Trim();
        environment.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
        environment.SortOrder = command.SortOrder;
        environment.IsDefault = command.IsDefault;
        environment.UpdatedAt = DateTime.UtcNow;

        if (command.IsDefault)
            await CreateEnvironmentHandler.ClearOtherDefaults(repository, command.TenantId, name, ct);

        await repository.SaveAsync(ct);

        return EnvironmentDto.From(environment);
    }
}
