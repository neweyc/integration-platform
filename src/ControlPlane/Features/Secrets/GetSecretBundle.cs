using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Secrets;

// Returns the secret manifest for a tenant + environment, delivered to a runtime agent at dispatch.
// Under the embedded backend entries carry decrypted values; under the external-vault backend they carry
// references the agent resolves locally. Served only to authenticated agents, not as a public endpoint.
public record GetSecretBundleCommand(Guid TenantId, string Environment) : ICommand<GetSecretBundleResult>;

public record GetSecretBundleResult(IReadOnlyList<SecretManifestEntry> Entries);

public class GetSecretBundleHandler(ISecretBackend backend)
    : ICommandHandler<GetSecretBundleCommand, GetSecretBundleResult>
{
    public async Task<GetSecretBundleResult> HandleAsync(GetSecretBundleCommand command, CancellationToken ct = default)
    {
        var manifest = await backend.GetManifestAsync(command.TenantId, command.Environment, ct);
        return new GetSecretBundleResult(manifest.Entries);
    }
}
