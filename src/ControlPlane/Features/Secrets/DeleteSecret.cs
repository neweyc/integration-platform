using ControlPlane.Infrastructure;

namespace ControlPlane.Features.Secrets;

public record DeleteSecretCommand(Guid TenantId, string Environment, string Key) : ICommand<bool>;

public interface ISecretDeleteRepository
{
    Task<bool> DeleteAsync(Guid tenantId, string environment, string key, CancellationToken ct = default);
}

public class DeleteSecretHandler(ISecretDeleteRepository repository)
    : ICommandHandler<DeleteSecretCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteSecretCommand command, CancellationToken ct = default)
    {
        var deleted = await repository.DeleteAsync(command.TenantId, command.Environment, command.Key, ct);

        if (!deleted)
            throw new NotFoundException($"Secret '{command.Key}' not found in environment '{command.Environment}'.");

        return true;
    }
}
