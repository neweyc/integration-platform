using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.UserTokens;

public record CreateUserTokenCommand(Guid TenantId, Guid UserId, string Name) : ICommand<CreateUserTokenResult>;

public record CreateUserTokenResult(Guid Id, string Name, string PlaintextToken, DateTime CreatedAt);

public class CreateUserTokenHandler(
    IUserTokenRepository repository,
    IUserTokenService tokenService)
    : ICommandHandler<CreateUserTokenCommand, CreateUserTokenResult>
{
    public async Task<CreateUserTokenResult> HandleAsync(CreateUserTokenCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ValidationException("Token name is required.");

        var plaintext = tokenService.Generate();
        var hash = tokenService.Hash(plaintext);

        var token = new UserToken
        {
            TenantId = command.TenantId,
            UserId = command.UserId,
            Name = command.Name,
            TokenHash = hash
        };

        var created = await repository.CreateAsync(token, ct);

        return new CreateUserTokenResult(created.Id, created.Name, plaintext, created.CreatedAt);
    }
}
