using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Secrets;

// Stores and resolves secret *values* for a tenant + environment. This is the seam between the control
// plane and where secret material actually lives.
//
// The EmbeddedSecretBackend below keeps values encrypted in the control-plane database — today's
// behavior. A future external-vault backend will keep only references in the control plane and resolve
// values from a vault running on the customer's own infrastructure, so secret material never rests in
// (a possibly hosted) control plane. See docs/secret-vault.md.
//
// Key/metadata listing stays on ISecretReadRepository: secret keys live in the control plane regardless
// of backend (the embedded backend stores key + value; an external backend would store key + reference).
public interface ISecretBackend
{
    // Create or update a secret's value. Returns the stored secret's id and timestamp.
    Task<SecretSetOutcome> SetAsync(Guid tenantId, string environment, string key, string value, CancellationToken ct = default);

    // Delete a secret. Returns false if it didn't exist.
    Task<bool> DeleteAsync(Guid tenantId, string environment, string key, CancellationToken ct = default);

    // Resolve all secret values for an environment — the bundle delivered to a runtime agent at dispatch.
    Task<IReadOnlyDictionary<string, string>> GetBundleAsync(Guid tenantId, string environment, CancellationToken ct = default);
}

public record SecretSetOutcome(Guid Id, DateTime UpdatedAt);

// Default backend: secret values are AES-encrypted and stored in the control-plane database, and
// decrypted there when resolving a bundle. Wraps the existing secret repository + encryption service,
// so behavior is identical to before this seam was introduced.
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

    public async Task<IReadOnlyDictionary<string, string>> GetBundleAsync(Guid tenantId, string environment, CancellationToken ct = default)
    {
        var secrets = await readRepository.ListAsync(tenantId, environment, ct);
        return secrets.ToDictionary(s => s.Key, s => encryption.Decrypt(s.EncryptedValue));
    }
}
