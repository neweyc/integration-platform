using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Features.UserTokens;

public record RevokeUserTokenCommand(Guid TenantId, Guid UserId, Guid TokenId) : ICommand<bool>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        result is true
            ? new(AuditAction.UserTokenRevoked, "UserToken", TokenId.ToString(), "Revoked personal access token")
            : null;
}

public class RevokeUserTokenHandler(IUserTokenRepository repository)
    : ICommandHandler<RevokeUserTokenCommand, bool>
{
    public async Task<bool> HandleAsync(RevokeUserTokenCommand command, CancellationToken ct = default)
    {
        return await repository.DeleteAsync(command.TenantId, command.UserId, command.TokenId, ct);
    }
}
