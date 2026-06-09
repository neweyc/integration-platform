using ControlPlane.Features.Environments;
using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public record CreateAgentTokenCommand(
    Guid TenantId,
    string Name,
    string Environment) : ICommand<CreateAgentTokenResult>, IAuditableCommand
{
    // Never include the token plaintext — only its id, name, and environment.
    public AuditDescriptor? Describe(object? result) =>
        new(AuditAction.AgentTokenCreated, "AgentToken",
            (result as CreateAgentTokenResult)?.Id.ToString(), $"Created agent token '{Name}' for {Environment}");
}

// Plaintext is returned exactly once — it is never stored and cannot be retrieved again
public record CreateAgentTokenResult(Guid Id, string Name, string Environment, string Token, DateTime CreatedAt);

public interface IAgentTokenRepository
{
    Task<AgentToken> CreateAsync(AgentToken token, CancellationToken ct = default);
}

public class CreateAgentTokenHandler(
    IAgentTokenRepository repository,
    IAgentTokenService tokenService,
    IEnvironmentReadRepository environments)
    : ICommandHandler<CreateAgentTokenCommand, CreateAgentTokenResult>
{
    public async Task<CreateAgentTokenResult> HandleAsync(CreateAgentTokenCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ValidationException("Name is required.");

        if (string.IsNullOrWhiteSpace(command.Environment))
            throw new ValidationException("Environment is required.");

        var environment = EnvironmentKey.Normalize(command.Environment);
        if (!await environments.ExistsAsync(command.TenantId, environment, ct))
            throw new ValidationException($"Environment '{environment}' does not exist. Create it before issuing a token scoped to it.");

        var plaintext = tokenService.Generate();

        var token = new AgentToken
        {
            TenantId = command.TenantId,
            Name = command.Name,
            Environment = environment,
            TokenHash = tokenService.Hash(plaintext),
        };

        var created = await repository.CreateAsync(token, ct);

        return new CreateAgentTokenResult(created.Id, created.Name, created.Environment, plaintext, created.CreatedAt);
    }
}
