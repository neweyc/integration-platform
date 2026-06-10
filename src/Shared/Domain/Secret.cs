namespace Shared.Domain;

public class Secret : Entity
{
    public Guid TenantId { get; set; }
    public string Environment { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;

    // Under the embedded backend this holds the AES-encrypted secret value. Under the external-vault
    // backend it is empty — the value lives in the vault on the customer's iron, not here.
    public string EncryptedValue { get; set; } = string.Empty;

    // Under the external-vault backend this holds an opaque reference (a path/handle the agent resolves
    // against the vault); the control plane never sees the value. Null/empty under the embedded backend.
    // See docs/secret-vault.md.
    public string? Reference { get; set; }

    public Tenant Tenant { get; set; } = null!;
}
