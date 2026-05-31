using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Secrets;

// Returns all secrets for a tenant + environment with values decrypted.
// This is used internally by the control plane when dispatching work to a runtime agent.
// It is NOT exposed as a public API endpoint.
public record GetSecretBundleCommand(Guid TenantId, string Environment) : ICommand<GetSecretBundleResult>;

public record GetSecretBundleResult(IReadOnlyDictionary<string, string> Secrets);

public class GetSecretBundleHandler(ISecretReadRepository repository, IEncryptionService encryption)
    : ICommandHandler<GetSecretBundleCommand, GetSecretBundleResult>
{
    public async Task<GetSecretBundleResult> HandleAsync(GetSecretBundleCommand command, CancellationToken ct = default)
    {
        var secrets = await repository.ListAsync(command.TenantId, command.Environment, ct);

        var decryptedSecrets = secrets.ToDictionary(
            keySelector: s => s.Key,
            elementSelector: s => encryption.Decrypt(s.EncryptedValue));

        return new GetSecretBundleResult(decryptedSecrets);
    }
}
