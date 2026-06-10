using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Setup;

// One-time setup endpoint. Creates the first tenant and admin user, then locks itself.
// Subsequent calls are rejected — if a tenant already exists, setup is considered complete.
public record SetupCommand(
    string TenantName,
    string TenantSlug,
    string AdminEmail,
    string AdminPassword) : ICommand<SetupResult>;

public record SetupResult(
    Guid TenantId, string TenantName, Guid UserId, string Email, string Token,
    string RefreshToken, DateTime RefreshTokenExpiresAt);

public interface ISetupRepository
{
    Task<bool> AnyTenantExistsAsync(CancellationToken ct = default);
    Task<Tenant> CreateTenantAsync(Tenant tenant, CancellationToken ct = default);
    Task<User> CreateUserAsync(User user, CancellationToken ct = default);
}

public class SetupHandler(ISetupRepository repository, IAuthTokenIssuer issuer)
    : ICommandHandler<SetupCommand, SetupResult>
{
    public async Task<SetupResult> HandleAsync(SetupCommand command, CancellationToken ct = default)
    {
        // Reject if setup has already been completed
        if (await repository.AnyTenantExistsAsync(ct))
            throw new ConflictException("Setup has already been completed.");

        ValidateCommand(command);

        var tenant = new Tenant
        {
            Name = command.TenantName,
            Slug = command.TenantSlug
        };

        var createdTenant = await repository.CreateTenantAsync(tenant, ct);

        var user = new User
        {
            TenantId = createdTenant.Id,
            Email = command.AdminEmail.ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(command.AdminPassword),
            Role = UserRole.Admin
        };

        var createdUser = await repository.CreateUserAsync(user, ct);

        var tokens = await issuer.IssueAsync(createdUser, ct);

        return new SetupResult(
            createdTenant.Id, createdTenant.Name, createdUser.Id, createdUser.Email,
            tokens.AccessToken, tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
    }

    private static void ValidateCommand(SetupCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.TenantName))
            throw new ValidationException("Tenant name is required.");

        if (string.IsNullOrWhiteSpace(command.TenantSlug))
            throw new ValidationException("Tenant slug is required.");

        if (!System.Text.RegularExpressions.Regex.IsMatch(command.TenantSlug, @"^[a-z0-9-]+$"))
            throw new ValidationException("Slug may only contain lowercase letters, numbers, and hyphens.");

        if (string.IsNullOrWhiteSpace(command.AdminEmail))
            throw new ValidationException("Admin email is required.");

        if (!IsValidEmail(command.AdminEmail))
            throw new ValidationException("Admin email is not valid.");

        if (string.IsNullOrWhiteSpace(command.AdminPassword) || command.AdminPassword.Length < 8)
            throw new ValidationException("Password must be at least 8 characters.");
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email.ToLowerInvariant();
        }
        catch
        {
            return false;
        }
    }
}
