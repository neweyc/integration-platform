namespace Shared.Domain;

public class Secret : Entity
{
    public Guid TenantId { get; set; }
    public string Environment { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string EncryptedValue { get; set; } = string.Empty;

    public Tenant Tenant { get; set; } = null!;
}
