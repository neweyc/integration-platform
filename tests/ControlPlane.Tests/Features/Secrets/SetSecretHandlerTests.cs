using ControlPlane.Features.Secrets;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Secrets;

public class SetSecretHandlerTests
{
    private readonly ISecretRepository _repository = Substitute.For<ISecretRepository>();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly SetSecretHandler _handler;

    private readonly Guid _tenantId = Guid.NewGuid();
    private const string Environment = "production";

    public SetSecretHandlerTests()
    {
        _handler = new SetSecretHandler(_repository, _encryption);

        // Default encryption behavior — just prepend "encrypted:" for easy test assertions
        _encryption.Encrypt(Arg.Any<string>()).Returns(call => $"encrypted:{call.Arg<string>()}");
    }

    [Fact]
    public async Task HandleAsync_NewSecret_CreatesAndReturnsResult()
    {
        var command = new SetSecretCommand(_tenantId, Environment, "API_KEY", "my-secret-value");

        _repository.FindAsync(_tenantId, Environment, "API_KEY").Returns((Secret?)null);
        _repository.CreateAsync(Arg.Any<Secret>()).Returns(call => call.Arg<Secret>());

        var result = await _handler.HandleAsync(command);

        Assert.Equal(Environment, result.Environment);
        Assert.Equal("API_KEY", result.Key);

        // Value must be encrypted before saving
        await _repository.Received(1).CreateAsync(Arg.Is<Secret>(s =>
            s.EncryptedValue == "encrypted:my-secret-value"));
    }

    [Fact]
    public async Task HandleAsync_ExistingSecret_UpdatesValue()
    {
        var existing = new Secret
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Environment = Environment,
            Key = "API_KEY",
            EncryptedValue = "encrypted:old-value"
        };

        var command = new SetSecretCommand(_tenantId, Environment, "API_KEY", "new-value");

        _repository.FindAsync(_tenantId, Environment, "API_KEY").Returns(existing);
        _repository.UpdateAsync(Arg.Any<Secret>()).Returns(call => call.Arg<Secret>());

        await _handler.HandleAsync(command);

        await _repository.Received(1).UpdateAsync(Arg.Is<Secret>(s =>
            s.EncryptedValue == "encrypted:new-value"));
        await _repository.DidNotReceive().CreateAsync(Arg.Any<Secret>());
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
}
