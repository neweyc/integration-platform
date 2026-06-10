using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Secrets;

// Stores and resolves secret material for a tenant + environment. This is the seam between the control
// plane and where secret values actually live.
//
// The EmbeddedSecretBackend keeps values encrypted in the control-plane database — today's behavior.
// The ExternalVaultSecretBackend keeps only references in the control plane; the actual values live in a
// vault running on the customer's own infrastructure, and the runtime agent resolves them at dispatch
// time. Under that backend secret material never rests in (a possibly hosted) control plane. See
// docs/secret-vault.md.
//
// Key/metadata listing stays on ISecretReadRepository: secret keys live in the control plane regardless
// of backend (the embedded backend stores key + value; the external backend stores key + reference).
public interface ISecretBackend
{
    // Create or update a secret. Returns the stored secret's id and timestamp.
    // Under the embedded backend `value` is the secret value (encrypted at rest here). Under the external
    // backend `value` is the vault reference that binds this key to a value living in the vault.
    Task<SecretSetOutcome> SetAsync(Guid tenantId, string environment, string key, string value, CancellationToken ct = default);

    // Delete a secret. Returns false if it didn't exist.
    Task<bool> DeleteAsync(Guid tenantId, string environment, string key, CancellationToken ct = default);

    // The manifest of secrets for an environment, delivered to a runtime agent at dispatch. Under the
    // embedded backend each entry is Inline (the decrypted value). Under the external backend each entry
    // is a Reference the agent resolves against the vault — the control plane never reads the value.
    Task<SecretManifest> GetManifestAsync(Guid tenantId, string environment, CancellationToken ct = default);
}

public record SecretSetOutcome(Guid Id, DateTime UpdatedAt);

// What the control plane hands a runtime agent for an environment. Inline entries carry the value
// directly (embedded backend); Reference entries carry a vault handle the agent resolves locally
// (external-vault backend). Keeping both shapes in one envelope lets the agent resolve uniformly
// regardless of which backend the control plane runs.
public record SecretManifest(IReadOnlyList<SecretManifestEntry> Entries);

public record SecretManifestEntry(string Key, SecretSource Source, string Payload);

// Serialized as a string ("Inline"/"Reference") so the agent's wire contract doesn't depend on numeric
// ordering — the attribute wins regardless of the endpoint's JSON options.
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum SecretSource
{
    // Payload is the secret value itself (embedded backend resolved it).
    Inline,

    // Payload is an opaque reference the agent resolves against its vault (external-vault backend).
    Reference
}

// Default backend: secret values are AES-encrypted and stored in the control-plane database, and
// decrypted there when building the manifest. Wraps the existing secret repository + encryption service.
public class EmbeddedSecretBackend(
    ISecretRepository repository,
    ISecretReadRepository readRepository,
    ISecretDeleteRepository deleteRepository,
    IEncryptionService encryption) : ISecretBackend
{
    public async Task<SecretSetOutcome> SetAsync(Guid tenantId, string environment, string key, string value, CancellationToken ct = default)
    {
        var encryptedValue = encryption.Encrypt(value);
        var existing = await repository.FindAsync(tenantId, environment, key, ct);

        if (existing is not null)
        {
            existing.EncryptedValue = encryptedValue;
            existing.Reference = null;
            existing.UpdatedAt = DateTime.UtcNow;
            var updated = await repository.UpdateAsync(existing, ct);
            return new SecretSetOutcome(updated.Id, updated.UpdatedAt);
        }

        var created = await repository.CreateAsync(new Secret
        {
            TenantId = tenantId,
            Environment = environment,
            Key = key,
            EncryptedValue = encryptedValue
        }, ct);

        return new SecretSetOutcome(created.Id, created.UpdatedAt);
    }

    public Task<bool> DeleteAsync(Guid tenantId, string environment, string key, CancellationToken ct = default) =>
        deleteRepository.DeleteAsync(tenantId, environment, key, ct);

    public async Task<SecretManifest> GetManifestAsync(Guid tenantId, string environment, CancellationToken ct = default)
    {
        var secrets = await readRepository.ListAsync(tenantId, environment, ct);
        var entries = secrets
            .Select(s => new SecretManifestEntry(s.Key, SecretSource.Inline, encryption.Decrypt(s.EncryptedValue)))
            .ToList();
        return new SecretManifest(entries);
    }
}

// External-vault backend: the control plane stores only references; the secret values live in a vault on
// the customer's infrastructure and are resolved by the runtime agent. The control plane never reads a
// value — this is what makes a hosted control plane adoptable under "no credentials off-prem".
//
// Operates in binding mode: SetAsync records the supplied vault reference (the value is written to the
// vault out-of-band, via its own tooling). The manifest carries those references for the agent to resolve.
public class ExternalVaultSecretBackend(
    ISecretRepository repository,
    ISecretReadRepository readRepository,
    ISecretDeleteRepository deleteRepository) : ISecretBackend
{
    public async Task<SecretSetOutcome> SetAsync(Guid tenantId, string environment, string key, string reference, CancellationToken ct = default)
    {
        var existing = await repository.FindAsync(tenantId, environment, key, ct);

        if (existing is not null)
        {
            existing.Reference = reference;
            existing.EncryptedValue = string.Empty;
            existing.UpdatedAt = DateTime.UtcNow;
            var updated = await repository.UpdateAsync(existing, ct);
            return new SecretSetOutcome(updated.Id, updated.UpdatedAt);
        }

        var created = await repository.CreateAsync(new Secret
        {
            TenantId = tenantId,
            Environment = environment,
            Key = key,
            Reference = reference
        }, ct);

        return new SecretSetOutcome(created.Id, created.UpdatedAt);
    }

    public Task<bool> DeleteAsync(Guid tenantId, string environment, string key, CancellationToken ct = default) =>
        deleteRepository.DeleteAsync(tenantId, environment, key, ct);

    public async Task<SecretManifest> GetManifestAsync(Guid tenantId, string environment, CancellationToken ct = default)
    {
        var secrets = await readRepository.ListAsync(tenantId, environment, ct);
        var entries = secrets
            .Select(s => new SecretManifestEntry(s.Key, SecretSource.Reference, s.Reference ?? string.Empty))
            .ToList();
        return new SecretManifest(entries);
    }
}
