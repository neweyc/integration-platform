using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Secrets;

// Creates a new secret or updates the value if the key already exists for this tenant + environment.
public record SetSecretCommand(
    Guid TenantId,
    string Environment,
    string Key,
    string Value) : ICommand<SetSecretResult>;

public record SetSecretResult(Guid Id, string Environment, string Key, DateTime UpdatedAt);

public interface ISecretRepository
{
    Task<Secret?> FindAsync(Guid tenantId, string environment, string key, CancellationToken ct = default);
    Task<Secret> CreateAsync(Secret secret, CancellationToken ct = default);
    Task<Secret> UpdateAsync(Secret secret, CancellationToken ct = default);
}

public class SetSecretHandler(ISecretRepository repository, IEncryptionService encryption)
    : ICommandHandler<SetSecretCommand, SetSecretResult>
{
    public async Task<SetSecretResult> HandleAsync(SetSecretCommand command, CancellationToken ct = default)
    {
        ValidateCommand(command);

        var encryptedValue = encryption.Encrypt(command.Value);
        var existing = await repository.FindAsync(command.TenantId, command.Environment, command.Key, ct);

        if (existing is not null)
        {
            // Update the existing secret's value
            existing.EncryptedValue = encryptedValue;
            existing.UpdatedAt = DateTime.UtcNow;
            var updated = await repository.UpdateAsync(existing, ct);
            return new SetSecretResult(updated.Id, updated.Environment, updated.Key, updated.UpdatedAt);
        }

        var newSecret = new Secret
        {
            TenantId = command.TenantId,
            Environment = command.Environment,
            Key = command.Key,
            EncryptedValue = encryptedValue
        };

        var created = await repository.CreateAsync(newSecret, ct);
        return new SetSecretResult(created.Id, created.Environment, created.Key, created.UpdatedAt);
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
