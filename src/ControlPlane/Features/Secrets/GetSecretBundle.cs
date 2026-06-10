using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Secrets;

// Returns all secrets for a tenant + environment with values decrypted.
// This is used internally by the control plane when dispatching work to a runtime agent.
// It is NOT exposed as a public API endpoint.
public record GetSecretBundleCommand(Guid TenantId, string Environment) : ICommand<GetSecretBundleResult>;

public record GetSecretBundleResult(IReadOnlyDictionary<string, string> Secrets);

public class GetSecretBundleHandler(ISecretBackend backend)
    : ICommandHandler<GetSecretBundleCommand, GetSecretBundleResult>
{
    public async Task<GetSecretBundleResult> HandleAsync(GetSecretBundleCommand command, CancellationToken ct = default)
    {
        var secrets = await backend.GetBundleAsync(command.TenantId, command.Environment, ct);
        return new GetSecretBundleResult(secrets);
    }
}
