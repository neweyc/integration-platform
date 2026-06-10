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
    public async Task HandleAsync_ReturnsBackendBundle()
    {
        _backend.GetBundleAsync(_tenantId, "production", Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string>
            {
                ["API_KEY"] = "real-api-key",
                ["DB_PASSWORD"] = "real-db-password"
            });

        var result = await _handler.HandleAsync(new GetSecretBundleCommand(_tenantId, "production"));

        Assert.Equal("real-api-key", result.Secrets["API_KEY"]);
        Assert.Equal("real-db-password", result.Secrets["DB_PASSWORD"]);
    }

    [Fact]
    public async Task HandleAsync_NoSecrets_ReturnsEmptyDictionary()
    {
        _backend.GetBundleAsync(_tenantId, "staging", Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string>());

        var result = await _handler.HandleAsync(new GetSecretBundleCommand(_tenantId, "staging"));

        Assert.Empty(result.Secrets);
    }
}
