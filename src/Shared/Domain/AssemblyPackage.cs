namespace Shared.Domain;

/// <summary>
/// Represents a compiled assembly package (.dll files in a zip) that contains integration implementations.
/// Packages are tenant-scoped and can contain multiple integration classes.
/// </summary>
public class AssemblyPackage : Entity
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// Human-readable name for the package (e.g. "MyCompany.Integrations").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Semantic version string (e.g. "1.0.0", "2.1.3-beta").
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Original filename of the uploaded zip file.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// The zip file contents containing compiled DLLs.
    /// </summary>
    public byte[] Data { get; set; } = [];

    /// <summary>
    /// Size of the package in bytes.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// SHA-256 hash of the package data for integrity verification.
    /// </summary>
    public string Sha256Hash { get; set; } = string.Empty;

    public Tenant Tenant { get; set; } = null!;
}
