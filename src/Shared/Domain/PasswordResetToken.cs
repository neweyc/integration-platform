namespace Shared.Domain;

// A single-use, short-lived credential emailed to a user who has forgotten their password. Only the
// SHA-256 hash is stored. The token is consumed (UsedAt set) the moment it successfully resets a
// password, and expires regardless after a short window.
public class PasswordResetToken : Entity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
