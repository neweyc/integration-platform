using ControlPlane.Features.Secrets;
using NSubstitute;

namespace ControlPlane.Tests.Features.Secrets;

public class GetSecretBundleHandlerTests
{
    private readonly ISecretBackend _backend = Substitute.For<ISecretBackend>();
    private readonly GetSecretBundleHandler _handler;

    private readonly Guid _tenantId = Guid.NewGuid();

    public GetSecretBundleHandlerTests()
    {
        _handler = new GetSecretBundleHandler(_backend);
    }

    [Fact]
    public async Task HandleAsync_ReturnsBackendManifestEntries()
    {
        _backend.GetManifestAsync(_tenantId, "production", Arg.Any<CancellationToken>())
            .Returns(new SecretManifest(new List<SecretManifestEntry>
            {
                new("API_KEY", SecretSource.Inline, "real-api-key"),
                new("DB_PASSWORD", SecretSource.Inline, "real-db-password")
            }));

        var result = await _handler.HandleAsync(new GetSecretBundleCommand(_tenantId, "production"));

        Assert.Equal("real-api-key", result.Entries.Single(e => e.Key == "API_KEY").Payload);
        Assert.Equal("real-db-password", result.Entries.Single(e => e.Key == "DB_PASSWORD").Payload);
    }

    [Fact]
    public async Task HandleAsync_PassesThroughReferenceEntries()
    {
        // Under the external-vault backend the handler must relay references untouched — the control plane
        // never resolves a value.
        _backend.GetManifestAsync(_tenantId, "production", Arg.Any<CancellationToken>())
            .Returns(new SecretManifest(new List<SecretManifestEntry>
            {
                new("API_KEY", SecretSource.Reference, "kv/production/api_key")
            }));

        var result = await _handler.HandleAsync(new GetSecretBundleCommand(_tenantId, "production"));

        var entry = Assert.Single(result.Entries);
        Assert.Equal(SecretSource.Reference, entry.Source);
        Assert.Equal("kv/production/api_key", entry.Payload);
    }

    [Fact]
    public async Task HandleAsync_NoSecrets_ReturnsEmptyEntries()
    {
        _backend.GetManifestAsync(_tenantId, "staging", Arg.Any<CancellationToken>())
            .Returns(new SecretManifest(new List<SecretManifestEntry>()));

        var result = await _handler.HandleAsync(new GetSecretBundleCommand(_tenantId, "staging"));

        Assert.Empty(result.Entries);
    }
}
