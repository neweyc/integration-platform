using ControlPlane.Features.Secrets;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Secrets;

public class GetSecretBundleHandlerTests
{
    private readonly ISecretReadRepository _repository = Substitute.For<ISecretReadRepository>();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly GetSecretBundleHandler _handler;

    private readonly Guid _tenantId = Guid.NewGuid();

    public GetSecretBundleHandlerTests()
    {
        _handler = new GetSecretBundleHandler(_repository, _encryption);
    }

    [Fact]
    public async Task HandleAsync_ReturnsDecryptedKeyValuePairs()
    {
        var secrets = new List<Secret>
        {
            new() { Key = "API_KEY", EncryptedValue = "enc:abc" },
            new() { Key = "DB_PASSWORD", EncryptedValue = "enc:xyz" }
        };

        _repository.ListAsync(_tenantId, "production").Returns(secrets);
        _encryption.Decrypt("enc:abc").Returns("real-api-key");
        _encryption.Decrypt("enc:xyz").Returns("real-db-password");

        var result = await _handler.HandleAsync(new GetSecretBundleCommand(_tenantId, "production"));

        Assert.Equal("real-api-key", result.Secrets["API_KEY"]);
        Assert.Equal("real-db-password", result.Secrets["DB_PASSWORD"]);
    }

    [Fact]
    public async Task HandleAsync_NoSecrets_ReturnsEmptyDictionary()
    {
        _repository.ListAsync(_tenantId, "staging").Returns(new List<Secret>());

        var result = await _handler.HandleAsync(new GetSecretBundleCommand(_tenantId, "staging"));

        Assert.Empty(result.Secrets);
    }
}
