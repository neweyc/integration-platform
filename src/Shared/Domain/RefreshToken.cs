namespace Shared.Domain;

// A long-lived credential that can be exchanged for a fresh short-lived access token. Only the
// SHA-256 hash of the token is stored. Tokens rotate on every use: the presented token is revoked
// and a successor issued, so a leaked token is usable at most once before the rotation is detected.
public class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }

    // Set when the token is rotated, logged out, or revoked. A non-null value means the token can
    // no longer be exchanged.
    public DateTime? RevokedAt { get; set; }

    // The hash of the token that replaced this one on rotation. Lets a presented-but-revoked token
    // be recognised as part of a known chain (reuse detection).
    public string? ReplacedByTokenHash { get; set; }
}
