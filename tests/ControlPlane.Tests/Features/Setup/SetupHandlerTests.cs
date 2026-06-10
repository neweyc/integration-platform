using ControlPlane.Features.Setup;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Setup;

public class SetupHandlerTests
{
    private readonly ISetupRepository _repository = Substitute.For<ISetupRepository>();
    private readonly IAuthTokenIssuer _issuer = Substitute.For<IAuthTokenIssuer>();
    private readonly SetupHandler _handler;

    public SetupHandlerTests()
    {
        _handler = new SetupHandler(_repository, _issuer);

        _repository.CreateTenantAsync(Arg.Any<Tenant>()).Returns(call => call.Arg<Tenant>());
        _repository.CreateUserAsync(Arg.Any<User>()).Returns(call => call.Arg<User>());
        _issuer.IssueAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(new AuthTokens("jwt-token", "rt_refresh", DateTime.UtcNow.AddDays(30)));
    }

    [Fact]
    public async Task HandleAsync_FirstRun_CreatesTenantAndAdminUserAndReturnsToken()
    {
        _repository.AnyTenantExistsAsync().Returns(false);

        var command = new SetupCommand("Acme Corp", "acme", "admin@acme.com", "securepass123");

        var result = await _handler.HandleAsync(command);

        Assert.Equal("Acme Corp", result.TenantName);
        Assert.Equal("admin@acme.com", result.Email);
        Assert.Equal("jwt-token", result.Token);

        await _repository.Received(1).CreateTenantAsync(Arg.Is<Tenant>(t =>
            t.Name == "Acme Corp" && t.Slug == "acme"));

        await _repository.Received(1).CreateUserAsync(Arg.Is<User>(u =>
            u.Email == "admin@acme.com" &&
            u.Role == UserRole.Admin &&
            BCrypt.Net.BCrypt.Verify("securepass123", u.PasswordHash)));
    }

    [Fact]
    public async Task HandleAsync_SetupAlreadyComplete_ThrowsConflictException()
    {
        _repository.AnyTenantExistsAsync().Returns(true);

        var command = new SetupCommand("Acme Corp", "acme", "admin@acme.com", "securepass123");

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(command));

        Assert.Equal("Setup has already been completed.", ex.Message);

        // Nothing should be created
        await _repository.DidNotReceive().CreateTenantAsync(Arg.Any<Tenant>());
        await _repository.DidNotReceive().CreateUserAsync(Arg.Any<User>());
    }

    [Theory]
    [InlineData("", "acme", "admin@acme.com", "pass1234", "Tenant name is required.")]
    [InlineData("Acme", "", "admin@acme.com", "pass1234", "Tenant slug is required.")]
    [InlineData("Acme", "Invalid Slug!", "admin@acme.com", "pass1234", "Slug may only contain lowercase letters, numbers, and hyphens.")]
    [InlineData("Acme", "acme", "", "pass1234", "Admin email is required.")]
    [InlineData("Acme", "acme", "not-an-email", "pass1234", "Admin email is not valid.")]
    [InlineData("Acme", "acme", "admin@acme.com", "short", "Password must be at least 8 characters.")]
    public async Task HandleAsync_InvalidInput_ThrowsValidationException(
        string tenantName, string slug, string email, string password, string expectedMessage)
    {
        _repository.AnyTenantExistsAsync().Returns(false);

        var command = new SetupCommand(tenantName, slug, email, password);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));

        Assert.Equal(expectedMessage, ex.Message);
    }
}
