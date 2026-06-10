using ControlPlane.Features.Auth;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Auth;

public class LoginUserHandlerTests
{
    private readonly IUserReadRepository _repository = Substitute.For<IUserReadRepository>();
    private readonly IAuthTokenIssuer _issuer = Substitute.For<IAuthTokenIssuer>();
    private readonly LoginUserHandler _handler;

    public LoginUserHandlerTests()
    {
        _handler = new LoginUserHandler(_repository, _issuer);
    }

    [Fact]
    public async Task HandleAsync_ValidCredentials_ReturnsToken()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Email = "admin@acme.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("securepass123"),
            Role = UserRole.Admin
        };

        _repository.GetByEmailAsync("admin@acme.com").Returns(user);
        _issuer.IssueAsync(user, Arg.Any<CancellationToken>())
            .Returns(new AuthTokens("jwt-token", "rt_refresh", DateTime.UtcNow.AddDays(30)));

        var result = await _handler.HandleAsync(new LoginUserCommand("admin@acme.com", "securepass123"));

        Assert.Equal("jwt-token", result.Token);
        Assert.Equal("admin@acme.com", result.Email);
        Assert.Equal("Admin", result.Role);
        Assert.Equal("rt_refresh", result.RefreshToken);
    }

    [Fact]
    public async Task HandleAsync_WrongPassword_ThrowsValidationException()
    {
        var user = new User
        {
            Email = "admin@acme.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correctpassword")
        };

        _repository.GetByEmailAsync("admin@acme.com").Returns(user);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _handler.HandleAsync(new LoginUserCommand("admin@acme.com", "wrongpassword")));

        Assert.Equal("Invalid email or password.", ex.Message);
    }

    [Fact]
    public async Task HandleAsync_UnknownEmail_ThrowsValidationException()
    {
        _repository.GetByEmailAsync(Arg.Any<string>()).Returns((User?)null);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _handler.HandleAsync(new LoginUserCommand("nobody@acme.com", "somepassword")));

        // Same message as wrong password — no user enumeration
        Assert.Equal("Invalid email or password.", ex.Message);
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("email@test.com", "")]
    public async Task HandleAsync_MissingCredentials_ThrowsValidationException(string email, string password)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _handler.HandleAsync(new LoginUserCommand(email, password)));
    }
}
