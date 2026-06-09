namespace Shared.Domain;

/// <summary>
/// The environment registry every new tenant starts with. A tenant is born with a single default
/// "production" environment so that auto-provisioning and the first integration have a valid target
/// without the operator having to create one first.
/// </summary>
public static class TenantEnvironments
{
    public const string DefaultName = "production";

    public static Environment Default(Guid tenantId) => new()
    {
        TenantId = tenantId,
        Name = DefaultName,
        DisplayName = "Production",
        SortOrder = 0,
        IsDefault = true
    };
}
