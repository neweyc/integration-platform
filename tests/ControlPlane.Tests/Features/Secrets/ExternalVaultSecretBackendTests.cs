using ControlPlane.Features.Secrets;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Secrets;

public class ExternalVaultSecretBackendTests
{
    private readonly ISecretRepository _repository = Substitute.For<ISecretRepository>();
    private readonly ISecretReadRepository _readRepository = Substitute.For<ISecretReadRepository>();
    private readonly ISecretDeleteRepository _deleteRepository = Substitute.For<ISecretDeleteRepository>();
    private readonly ExternalVaultSecretBackend _backend;

    private readonly Guid _tenantId = Guid.NewGuid();
    private const string Environment = "production";

    public ExternalVaultSecretBackendTests()
    {
        _backend = new ExternalVaultSecretBackend(_repository, _readRepository, _deleteRepository);
    }

    [Fact]
    public async Task SetAsync_NewBinding_StoresReferenceNotValue()
    {
        _repository.FindAsync(_tenantId, Environment, "API_KEY").Returns((Secret?)null);
        _repository.CreateAsync(Arg.Any<Secret>()).Returns(call => call.Arg<Secret>());

        await _backend.SetAsync(_tenantId, Environment, "API_KEY", "kv/production/api_key");

        // The reference is recorded; the value never touches the control plane, so EncryptedValue stays empty.
        await _repository.Received(1).CreateAsync(Arg.Is<Secret>(s =>
            s.Key == "API_KEY"
            && s.Reference == "kv/production/api_key"
            && s.EncryptedValue == string.Empty));
    }

    [Fact]
    public async Task SetAsync_ExistingSecret_UpdatesReferenceAndClearsValue()
    {
        var existing = new Secret
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Environment = Environment,
            Key = "API_KEY",
            EncryptedValue = "encrypted:leftover-from-embedded"
        };
        _repository.FindAsync(_tenantId, Environment, "API_KEY").Returns(existing);
        _repository.UpdateAsync(Arg.Any<Secret>()).Returns(call => call.Arg<Secret>());

        await _backend.SetAsync(_tenantId, Environment, "API_KEY", "kv/production/api_key");

        await _repository.Received(1).UpdateAsync(Arg.Is<Secret>(s =>
            s.Reference == "kv/production/api_key" && s.EncryptedValue == string.Empty));
    }

    [Fact]
    public async Task GetManifestAsync_EmitsReferenceEntries()
    {
        _readRepository.ListAsync(_tenantId, Environment).Returns(new List<Secret>
        {
            new() { Key = "API_KEY", Reference = "kv/production/api_key" },
            new() { Key = "DB_PASSWORD", Reference = "kv/production/db_password" }
        });

        var manifest = await _backend.GetManifestAsync(_tenantId, Environment);

        // External backend never resolves: every entry is a Reference the agent will resolve against the vault.
        Assert.All(manifest.Entries, e => Assert.Equal(SecretSource.Reference, e.Source));
        Assert.Equal("kv/production/api_key", manifest.Entries.Single(e => e.Key == "API_KEY").Payload);
        Assert.Equal("kv/production/db_password", manifest.Entries.Single(e => e.Key == "DB_PASSWORD").Payload);
    }

    [Fact]
    public async Task DeleteAsync_DelegatesToRepository()
    {
        _deleteRepository.DeleteAsync(_tenantId, Environment, "API_KEY").Returns(true);

        Assert.True(await _backend.DeleteAsync(_tenantId, Environment, "API_KEY"));
    }
}
