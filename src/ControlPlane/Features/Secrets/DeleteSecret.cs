using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Features.Secrets;

public record DeleteSecretCommand(Guid TenantId, string Environment, string Key) : ICommand<bool>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        new(AuditAction.SecretDeleted, "Secret", $"{Environment}/{Key}", $"Deleted secret '{Key}' in {Environment}");
}

public interface ISecretDeleteRepository
{
    Task<bool> DeleteAsync(Guid tenantId, string environment, string key, CancellationToken ct = default);
}

public class DeleteSecretHandler(ISecretBackend backend)
    : ICommandHandler<DeleteSecretCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteSecretCommand command, CancellationToken ct = default)
    {
        var deleted = await backend.DeleteAsync(command.TenantId, command.Environment, command.Key, ct);

        if (!deleted)
            throw new NotFoundException($"Secret '{command.Key}' not found in environment '{command.Environment}'.");

        return true;
    }
}
