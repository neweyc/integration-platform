using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RuntimeAgent.Agent;

// Agent-side mirror of the control plane's secret manifest wire contract (the agent doesn't reference the
// control plane). An Inline entry carries the value; a Reference entry carries a vault handle to resolve.
public record SecretManifestEntry(string Key, SecretSource Source, string Payload);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SecretSource
{
    Inline,
    Reference
}

// Resolves a secret reference handed down by the control plane (under the external-vault backend) into
// the actual secret value, by reading from the vault on the customer's own network. The control plane is
// never in the path of the value — see docs/secret-vault.md.
public interface IVaultClient
{
    Task<string> ResolveAsync(string reference, CancellationToken ct);
}

// Used when no vault is configured (the embedded backend, where the control plane already resolves
// values). The agent should never receive a Reference entry in that mode; if it does, fail loudly rather
// than silently dropping the secret.
public class NullVaultClient : IVaultClient
{
    public Task<string> ResolveAsync(string reference, CancellationToken ct) =>
        throw new InvalidOperationException(
            $"The control plane returned a vault reference ('{reference}') but no vault is configured. " +
            "Set Agent:VaultAddress when the control plane uses the external-vault secret backend.");
}

// Reference implementation: reads a value from a generic HTTP key-value vault. It GETs
// {VaultAddress}/{reference} and expects a JSON body of the form { "value": "<secret>" }. This is the
// integration seam — adapting it to OpenBao / HashiCorp Vault's KV API is a thin change (the vault
// container itself is the next rollout step). See docs/secret-vault.md.
public class HttpVaultClient(HttpClient http, ILogger<HttpVaultClient> logger) : IVaultClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> ResolveAsync(string reference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new InvalidOperationException("Vault reference is empty; the control plane stored no binding for this key.");

        try
        {
            var response = await http.GetFromJsonAsync<VaultValue>(reference.TrimStart('/'), JsonOptions, ct);
            if (response is null || response.Value is null)
                throw new InvalidOperationException($"Vault returned no value for reference '{reference}'.");

            return response.Value;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to resolve secret reference {Reference} from the vault", reference);
            throw new InvalidOperationException($"Could not resolve secret reference '{reference}' from the vault.", ex);
        }
    }

    private record VaultValue(string? Value);
}
