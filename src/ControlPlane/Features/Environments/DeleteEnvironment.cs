using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Features.Environments;

public record DeleteEnvironmentCommand(Guid TenantId, string Name) : ICommand<bool>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        new(AuditAction.EnvironmentDeleted, "Environment",
            EnvironmentKey.Normalize(Name), $"Deleted environment '{EnvironmentKey.Normalize(Name)}'");
}

public class DeleteEnvironmentHandler(IEnvironmentWriteRepository repository)
    : ICommandHandler<DeleteEnvironmentCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteEnvironmentCommand command, CancellationToken ct = default)
    {
        var name = EnvironmentKey.Normalize(command.Name);
        var environment = await repository.FindAsync(command.TenantId, name, ct)
            ?? throw new NotFoundException($"Environment '{name}' not found.");

        // The default environment must always exist (package auto-provisioning targets it), so it can't
        // be deleted until another environment is made the default.
        if (environment.IsDefault)
            throw new ConflictException(
                $"Environment '{name}' is the default and cannot be deleted. Make another environment the default first.");

        // Refuse to delete an environment that still has live configuration pointing at it. This is the
        // lifecycle guard the old free-form-string model lacked; the database FK is the backstop, but
        // checking here lets us return a specific, actionable reason.
        var usage = await repository.GetUsageAsync(command.TenantId, name, ct);
        if (usage.InUse)
        {
            var parts = new List<string>();
            if (usage.Integrations > 0) parts.Add($"{usage.Integrations} integration(s)");
            if (usage.Secrets > 0) parts.Add($"{usage.Secrets} secret(s)");
            if (usage.AgentTokens > 0) parts.Add($"{usage.AgentTokens} agent token(s)");
            if (usage.Workflows > 0) parts.Add($"{usage.Workflows} workflow(s)");

            throw new ConflictException(
                $"Environment '{name}' is still in use by {string.Join(", ", parts)}. " +
                "Move or remove them before deleting the environment.");
        }

        repository.Remove(environment);
        await repository.SaveAsync(ct);
        return true;
    }
}
