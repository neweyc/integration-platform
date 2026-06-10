using ControlPlane.Features.Secrets;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Secrets;

public class EmbeddedSecretBackendTests
{
    private readonly ISecretRepository _repository = Substitute.For<ISecretRepository>();
    private readonly ISecretReadRepository _readRepository = Substitute.For<ISecretReadRepository>();
    private readonly ISecretDeleteRepository _deleteRepository = Substitute.For<ISecretDeleteRepository>();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly EmbeddedSecretBackend _backend;

    private readonly Guid _tenantId = Guid.NewGuid();
    private const string Environment = "production";

    public EmbeddedSecretBackendTests()
    {
        _backend = new EmbeddedSecretBackend(_repository, _readRepository, _deleteRepository, _encryption);
        _encryption.Encrypt(Arg.Any<string>()).Returns(call => $"encrypted:{call.Arg<string>()}");
    }

    [Fact]
    public async Task SetAsync_NewSecret_EncryptsAndCreates()
    {
        _repository.FindAsync(_tenantId, Environment, "API_KEY").Returns((Secret?)null);
        _repository.CreateAsync(Arg.Any<Secret>()).Returns(call => call.Arg<Secret>());

        await _backend.SetAsync(_tenantId, Environment, "API_KEY", "my-secret-value");

        await _repository.Received(1).CreateAsync(Arg.Is<Secret>(s =>
            s.TenantId == _tenantId
            && s.Environment == Environment
            && s.Key == "API_KEY"
            && s.EncryptedValue == "encrypted:my-secret-value"));
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<Secret>());
    }

    [Fact]
    public async Task SetAsync_ExistingSecret_UpdatesEncryptedValue()
    {
        var existing = new Secret
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Environment = Environment,
            Key = "API_KEY",
            EncryptedValue = "encrypted:old-value"
        };
        _repository.FindAsync(_tenantId, Environment, "API_KEY").Returns(existing);
        _repository.UpdateAsync(Arg.Any<Secret>()).Returns(call => call.Arg<Secret>());

        await _backend.SetAsync(_tenantId, Environment, "API_KEY", "new-value");

        await _repository.Received(1).UpdateAsync(Arg.Is<Secret>(s => s.EncryptedValue == "encrypted:new-value"));
        await _repository.DidNotReceive().CreateAsync(Arg.Any<Secret>());
    }

    [Fact]
    public async Task GetManifestAsync_DecryptsAllValuesAsInlineEntries()
    {
        _readRepository.ListAsync(_tenantId, Environment).Returns(new List<Secret>
        {
            new() { Key = "API_KEY", EncryptedValue = "enc:abc" },
            new() { Key = "DB_PASSWORD", EncryptedValue = "enc:xyz" }
        });
        _encryption.Decrypt("enc:abc").Returns("real-api-key");
        _encryption.Decrypt("enc:xyz").Returns("real-db-password");

        var manifest = await _backend.GetManifestAsync(_tenantId, Environment);

        // Embedded backend resolves values here: every entry is Inline carrying the decrypted value.
        Assert.All(manifest.Entries, e => Assert.Equal(SecretSource.Inline, e.Source));
        Assert.Equal("real-api-key", manifest.Entries.Single(e => e.Key == "API_KEY").Payload);
        Assert.Equal("real-db-password", manifest.Entries.Single(e => e.Key == "DB_PASSWORD").Payload);
    }

    [Fact]
    public async Task DeleteAsync_DelegatesToRepository()
    {
        _deleteRepository.DeleteAsync(_tenantId, Environment, "API_KEY").Returns(true);

        Assert.True(await _backend.DeleteAsync(_tenantId, Environment, "API_KEY"));
    }
}
