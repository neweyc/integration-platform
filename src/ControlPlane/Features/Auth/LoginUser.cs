using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Auth;

public record LoginUserCommand(string Email, string Password) : ICommand<LoginUserResult>;

public record LoginUserResult(string Token, string Email, string Role);

public interface IUserReadRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
}

public class LoginUserHandler(IUserReadRepository repository, IJwtTokenService tokenService)
    : ICommandHandler<LoginUserCommand, LoginUserResult>
{
    public async Task<LoginUserResult> HandleAsync(LoginUserCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Email) || string.IsNullOrWhiteSpace(command.Password))
            throw new ValidationException("Email and password are required.");

        var user = await repository.GetByEmailAsync(command.Email.ToLowerInvariant(), ct);

        // Same error message for not found vs wrong password — no user enumeration
        if (user is null || !BCrypt.Net.BCrypt.Verify(command.Password, user.PasswordHash))
            throw new ValidationException("Invalid email or password.");

        var token = tokenService.GenerateToken(user);

        return new LoginUserResult(token, user.Email, user.Role.ToString());
    }
}
