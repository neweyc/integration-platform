using ControlPlane.Features.Environments;
using ControlPlane.Features.Secrets;
using ControlPlane.Infrastructure;
using NSubstitute;

namespace ControlPlane.Tests.Features.Secrets;

public class SetSecretHandlerTests
{
    private readonly ISecretBackend _backend = Substitute.For<ISecretBackend>();
    private readonly IEnvironmentReadRepository _environments = Substitute.For<IEnvironmentReadRepository>();
    private readonly SetSecretHandler _handler;

    private readonly Guid _tenantId = Guid.NewGuid();
    private const string Environment = "production";

    public SetSecretHandlerTests()
    {
        _handler = new SetSecretHandler(_backend, _environments);

        // The environment exists by default; specific tests override this to assert the unknown-env guard.
        _environments.ExistsAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        _backend.SetAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SecretSetOutcome(Guid.NewGuid(), DateTime.UtcNow));
    }

    [Fact]
    public async Task HandleAsync_ValidSecret_StoresViaBackendAndReturnsResult()
    {
        var command = new SetSecretCommand(_tenantId, Environment, "API_KEY", "my-secret-value");

        var result = await _handler.HandleAsync(command);

        Assert.Equal(Environment, result.Environment);
        Assert.Equal("API_KEY", result.Key);
        await _backend.Received(1).SetAsync(
            _tenantId, Environment, "API_KEY", "my-secret-value", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("API_KEY", "", "Value cannot be empty.")]
    [InlineData("", "value", "Key is required.")]
    [InlineData("invalid-key", "value", "Key must start with a letter and contain only uppercase letters, numbers, and underscores.")]
    [InlineData("1_INVALID", "value", "Key must start with a letter and contain only uppercase letters, numbers, and underscores.")]
    public async Task HandleAsync_InvalidInput_ThrowsValidationException(string key, string value, string expectedMessage)
    {
        var command = new SetSecretCommand(_tenantId, Environment, key, value);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));

        Assert.Equal(expectedMessage, ex.Message);
    }

    [Fact]
    public async Task HandleAsync_MissingEnvironment_ThrowsValidationException()
    {
        var command = new SetSecretCommand(_tenantId, "", "API_KEY", "value");

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));
    }

    [Fact]
    public async Task HandleAsync_UnknownEnvironment_ThrowsAndDoesNotStore()
    {
        // The environment is not in the tenant's registry — the write must be rejected, not silently
        // create a ghost environment.
        _environments.ExistsAsync(_tenantId, "staging", Arg.Any<CancellationToken>()).Returns(false);
        var command = new SetSecretCommand(_tenantId, "staging", "API_KEY", "value");

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));

        Assert.Contains("does not exist", ex.Message);
        await _backend.DidNotReceive().SetAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
