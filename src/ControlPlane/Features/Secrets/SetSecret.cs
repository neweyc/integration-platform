using ControlPlane.Features.Environments;
using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Features.Secrets;

// Creates a new secret or updates the value if the key already exists for this tenant + environment.
public record SetSecretCommand(
    Guid TenantId,
    string Environment,
    string Key,
    string Value) : ICommand<SetSecretResult>, IAuditableCommand
{
    // Never include the secret value — only which key/environment changed.
    public AuditDescriptor? Describe(object? result) =>
        new(AuditAction.SecretSet, "Secret", $"{Environment}/{Key}", $"Set secret '{Key}' in {Environment}");
}

public record SetSecretResult(Guid Id, string Environment, string Key, DateTime UpdatedAt);

public interface ISecretRepository
{
    Task<Secret?> FindAsync(Guid tenantId, string environment, string key, CancellationToken ct = default);
    Task<Secret> CreateAsync(Secret secret, CancellationToken ct = default);
    Task<Secret> UpdateAsync(Secret secret, CancellationToken ct = default);
}

public class SetSecretHandler(
    ISecretBackend backend,
    IEnvironmentReadRepository environments)
    : ICommandHandler<SetSecretCommand, SetSecretResult>
{
    public async Task<SetSecretResult> HandleAsync(SetSecretCommand command, CancellationToken ct = default)
    {
        ValidateCommand(command);

        var environment = EnvironmentKey.Normalize(command.Environment);
        if (!await environments.ExistsAsync(command.TenantId, environment, ct))
            throw new ValidationException($"Environment '{environment}' does not exist. Create it before setting secrets for it.");

        var outcome = await backend.SetAsync(command.TenantId, environment, command.Key, command.Value, ct);
        return new SetSecretResult(outcome.Id, environment, command.Key, outcome.UpdatedAt);
    }

    private static void ValidateCommand(SetSecretCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Environment))
            throw new ValidationException("Environment is required.");

        if (string.IsNullOrWhiteSpace(command.Key))
            throw new ValidationException("Key is required.");

        // Keys follow the same convention as environment variables: uppercase letters, numbers, underscores
        if (!System.Text.RegularExpressions.Regex.IsMatch(command.Key, @"^[A-Z][A-Z0-9_]*$"))
            throw new ValidationException("Key must start with a letter and contain only uppercase letters, numbers, and underscores.");

        if (string.IsNullOrEmpty(command.Value))
            throw new ValidationException("Value cannot be empty.");
    }
}
