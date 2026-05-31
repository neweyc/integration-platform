using ControlPlane.Features.Auth;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Auth;

public class RegisterUserHandlerTests
{
    private readonly IUserRepository _repository = Substitute.For<IUserRepository>();
    private readonly RegisterUserHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();

    public RegisterUserHandlerTests()
    {
        _handler = new RegisterUserHandler(_repository);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_FirstUser_CreatesAdminUser()
    {
        var command = new RegisterUserCommand(_tenantId, "admin@acme.com", "securepass123");

        _repository.EmailExistsAsync(_tenantId, "admin@acme.com").Returns(false);
        _repository.HasAnyAdminAsync(_tenantId).Returns(false);
        _repository.CreateAsync(Arg.Any<User>()).Returns(call => call.Arg<User>());

        var result = await _handler.HandleAsync(command);

        Assert.Equal("admin@acme.com", result.Email);
        await _repository.Received(1).CreateAsync(Arg.Is<User>(u =>
            u.Role == UserRole.Admin &&
            u.TenantId == _tenantId &&
            u.Email == "admin@acme.com"));
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_SubsequentUser_CreatesMemberUser()
    {
        var command = new RegisterUserCommand(_tenantId, "member@acme.com", "securepass123");

        _repository.EmailExistsAsync(_tenantId, "member@acme.com").Returns(false);
        _repository.HasAnyAdminAsync(_tenantId).Returns(true);
        _repository.CreateAsync(Arg.Any<User>()).Returns(call => call.Arg<User>());

        await _handler.HandleAsync(command);

        await _repository.Received(1).CreateAsync(Arg.Is<User>(u => u.Role == UserRole.Member));
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_PasswordIsHashed()
    {
        var command = new RegisterUserCommand(_tenantId, "user@acme.com", "securepass123");

        _repository.EmailExistsAsync(_tenantId, "user@acme.com").Returns(false);
        _repository.HasAnyAdminAsync(_tenantId).Returns(false);
        _repository.CreateAsync(Arg.Any<User>()).Returns(call => call.Arg<User>());

        await _handler.HandleAsync(command);

        await _repository.Received(1).CreateAsync(Arg.Is<User>(u =>
            u.PasswordHash != "securepass123" &&
            BCrypt.Net.BCrypt.Verify("securepass123", u.PasswordHash)));
    }

    [Theory]
    [InlineData("", "pass1234", "Email is required.")]
    [InlineData("not-an-email", "pass1234", "Email is not valid.")]
    [InlineData("user@acme.com", "", "Password must be at least 8 characters.")]
    [InlineData("user@acme.com", "short", "Password must be at least 8 characters.")]
    public async Task HandleAsync_InvalidInput_ThrowsValidationException(string email, string password, string expectedMessage)
    {
        var command = new RegisterUserCommand(_tenantId, email, password);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));

        Assert.Equal(expectedMessage, ex.Message);
    }

    [Fact]
    public async Task HandleAsync_DuplicateEmail_ThrowsConflictException()
    {
        var command = new RegisterUserCommand(_tenantId, "admin@acme.com", "securepass123");
        _repository.EmailExistsAsync(_tenantId, "admin@acme.com").Returns(true);

        await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(command));
    }
}
