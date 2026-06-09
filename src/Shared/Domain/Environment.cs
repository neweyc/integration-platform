namespace Shared.Domain;

/// <summary>
/// A deployment environment (e.g. production, staging) that a tenant's secrets, integrations,
/// agent tokens, and workflows are scoped to. Environments are first-class and per-tenant:
/// they form the canonical registry every other environment-scoped record is validated against,
/// which is what turns a mistyped environment string into a clear error instead of a silent ghost.
///
/// <see cref="Name"/> is the canonical key (lowercase) referenced by the string Environment columns
/// on the scoped entities; <see cref="DisplayName"/> is the human-friendly label shown in the UI.
/// </summary>
public class Environment : Entity
{
    public Guid TenantId { get; set; }

    // Canonical, lowercase identifier (e.g. "production"). Unique within a tenant and used as the
    // value stored in the Environment columns of secrets, integrations, agent tokens, and workflows.
    public string Name { get; set; } = string.Empty;

    // Human-friendly label (e.g. "Production"). Free-form; defaults to the name when not supplied.
    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    // Controls ordering in the UI; lower sorts first.
    public int SortOrder { get; set; }

    // The environment offered as the default selection (e.g. for new integrations and auto-provisioning).
    // Exactly one per tenant is expected, but this is not enforced at the database level.
    public bool IsDefault { get; set; }

    public Tenant Tenant { get; set; } = null!;
}
