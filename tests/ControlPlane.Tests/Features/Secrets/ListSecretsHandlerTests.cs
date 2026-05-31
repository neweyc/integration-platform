using ControlPlane.Features.Secrets;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Secrets;

public class ListSecretsHandlerTests
{
    private readonly ISecretReadRepository _repository = Substitute.For<ISecretReadRepository>();
    private readonly ListSecretsHandler _handler;

    private readonly Guid _tenantId = Guid.NewGuid();

    public ListSecretsHandlerTests()
    {
        _handler = new ListSecretsHandler(_repository);
    }

    [Fact]
    public async Task HandleAsync_ReturnsSecretKeysWithoutValues()
    {
        var secrets = new List<Secret>
        {
            new() { Id = Guid.NewGuid(), Key = "API_KEY", EncryptedValue = "encrypted:should-not-appear" },
            new() { Id = Guid.NewGuid(), Key = "DB_PASSWORD", EncryptedValue = "encrypted:should-not-appear" }
        };

        _repository.ListAsync(_tenantId, "production").Returns(secrets);

        var result = await _handler.HandleAsync(new ListSecretsCommand(_tenantId, "production"));

        Assert.Equal(2, result.Secrets.Count);
        Assert.Contains(result.Secrets, s => s.Key == "API_KEY");
        Assert.Contains(result.Secrets, s => s.Key == "DB_PASSWORD");

        // Confirm no encrypted values leak through to the result
        Assert.All(result.Secrets, s => Assert.DoesNotContain("encrypted", s.Key));
    }

    [Fact]
    public async Task HandleAsync_MissingEnvironment_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _handler.HandleAsync(new ListSecretsCommand(_tenantId, "")));
    }

    [Fact]
    public async Task HandleAsync_NoSecrets_ReturnsEmptyList()
    {
        _repository.ListAsync(_tenantId, "staging").Returns(new List<Secret>());

        var result = await _handler.HandleAsync(new ListSecretsCommand(_tenantId, "staging"));

        Assert.Empty(result.Secrets);
    }
}
