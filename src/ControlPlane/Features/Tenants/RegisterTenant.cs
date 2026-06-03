using ControlPlane.Infrastructure;
using Shared.Domain;
using ControlPlane.Features.Auth;

namespace ControlPlane.Features.Tenants;

public record RegisterTenantCommand(
    string TenantName,
    string TenantSlug,
    string AdminEmail,
    string AdminPassword) : ICommand<RegisterTenantResult>;

public record RegisterTenantResult(Guid TenantId, string TenantName, Guid UserId, string Email, string Token);

public class RegisterTenantHandler(
    ITenantRepository tenantRepository,
    IUserRepository userRepository,
    IJwtTokenService tokenService)
    : ICommandHandler<RegisterTenantCommand, RegisterTenantResult>
{
    public async Task<RegisterTenantResult> HandleAsync(RegisterTenantCommand command, CancellationToken ct = default)
    {
        ValidateCommand(command);

        if (await tenantRepository.SlugExistsAsync(command.TenantSlug, ct))
            throw new ConflictException($"Slug '{command.TenantSlug}' is already taken.");

        var tenant = new Tenant
        {
            Name = command.TenantName,
            Slug = command.TenantSlug
        };

        var createdTenant = await tenantRepository.CreateAsync(tenant, ct);

        var user = new User
        {
            TenantId = createdTenant.Id,
            Email = command.AdminEmail.ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(command.AdminPassword),
            Role = UserRole.Admin
        };

        var createdUser = await userRepository.CreateAsync(user, ct);

        var token = tokenService.GenerateToken(createdUser);

        return new RegisterTenantResult(createdTenant.Id, createdTenant.Name, createdUser.Id, createdUser.Email, token);
    }

    private static void ValidateCommand(RegisterTenantCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.TenantName))
            throw new ValidationException("Tenant name is required.");

        if (string.IsNullOrWhiteSpace(command.TenantSlug))
            throw new ValidationException("Tenant slug is required.");

        if (!System.Text.RegularExpressions.Regex.IsMatch(command.TenantSlug, @"^[a-z0-9-]+$"))
            throw new ValidationException("Slug may only contain lowercase letters, numbers, and hyphens.");

        if (string.IsNullOrWhiteSpace(command.AdminEmail))
            throw new ValidationException("Admin email is required.");

        if (string.IsNullOrWhiteSpace(command.AdminPassword) || command.AdminPassword.Length < 8)
            throw new ValidationException("Password must be at least 8 characters.");
    }
}
