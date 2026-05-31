using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Auth;

public record RegisterUserCommand(Guid TenantId, string Email, string Password) : ICommand<RegisterUserResult>;

public record RegisterUserResult(Guid UserId, string Email);

public interface IUserRepository
{
    Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken ct = default);
    Task<User> CreateAsync(User user, CancellationToken ct = default);
    Task<bool> HasAnyAdminAsync(Guid tenantId, CancellationToken ct = default);
}

public class RegisterUserHandler(IUserRepository repository)
    : ICommandHandler<RegisterUserCommand, RegisterUserResult>
{
    public async Task<RegisterUserResult> HandleAsync(RegisterUserCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Email))
            throw new ValidationException("Email is required.");

        if (!IsValidEmail(command.Email))
            throw new ValidationException("Email is not valid.");

        if (string.IsNullOrWhiteSpace(command.Password) || command.Password.Length < 8)
            throw new ValidationException("Password must be at least 8 characters.");

        if (await repository.EmailExistsAsync(command.TenantId, command.Email, ct))
            throw new ConflictException($"Email '{command.Email}' is already registered.");

        var isFirstAdmin = !await repository.HasAnyAdminAsync(command.TenantId, ct);

        var user = new User
        {
            TenantId = command.TenantId,
            Email = command.Email.ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(command.Password),
            Role = isFirstAdmin ? UserRole.Admin : UserRole.Member
        };

        var created = await repository.CreateAsync(user, ct);

        return new RegisterUserResult(created.Id, created.Email);
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
