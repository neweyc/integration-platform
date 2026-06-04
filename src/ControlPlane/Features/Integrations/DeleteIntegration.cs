using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

public record DeleteIntegrationCommand(Guid TenantId, Guid IntegrationId) : ICommand<bool>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        new(AuditAction.IntegrationDeleted, "Integration", IntegrationId.ToString(), "Deleted integration");
}

public interface IIntegrationDeleteRepository
{
    Task<bool> DeleteAsync(Guid tenantId, Guid integrationId, CancellationToken ct = default);
}

public class DeleteIntegrationHandler(IIntegrationDeleteRepository repository)
    : ICommandHandler<DeleteIntegrationCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteIntegrationCommand command, CancellationToken ct = default)
    {
        var deleted = await repository.DeleteAsync(command.TenantId, command.IntegrationId, ct);

        if (!deleted)
            throw new NotFoundException($"Integration '{command.IntegrationId}' not found.");

        return true;
    }
}
