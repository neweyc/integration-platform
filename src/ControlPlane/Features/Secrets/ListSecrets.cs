using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Secrets;

public record ListSecretsCommand(Guid TenantId, string Environment) : ICommand<ListSecretsResult>;

// Secret keys and metadata are returned — never the plaintext values
public record ListSecretsResult(IReadOnlyList<SecretSummary> Secrets);
public record SecretSummary(Guid Id, string Key, DateTime UpdatedAt);

public interface ISecretReadRepository
{
    Task<IReadOnlyList<Secret>> ListAsync(Guid tenantId, string environment, CancellationToken ct = default);
}

public class ListSecretsHandler(ISecretReadRepository repository)
    : ICommandHandler<ListSecretsCommand, ListSecretsResult>
{
    public async Task<ListSecretsResult> HandleAsync(ListSecretsCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Environment))
            throw new ValidationException("Environment is required.");

        var secrets = await repository.ListAsync(command.TenantId, command.Environment, ct);

        var summaries = secrets
            .Select(s => new SecretSummary(s.Id, s.Key, s.UpdatedAt))
            .ToList();

        return new ListSecretsResult(summaries);
    }
}
