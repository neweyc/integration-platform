using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RuntimeAgent.Agent;

namespace RuntimeAgent.Tests;

public class ControlPlaneClientSecretsTests
{
    [Fact]
    public async Task GetSecretsAsync_InlineEntries_ReturnValueDirectlyWithoutHittingVault()
    {
        var manifest = """
            { "entries": [ { "key": "API_KEY", "source": "Inline", "payload": "s3cr3t" } ] }
            """;
        var vault = new RecordingVaultClient();
        var client = BuildClient(manifest, vault);

        var secrets = await client.GetSecretsAsync(CancellationToken.None);

        Assert.Equal("s3cr3t", secrets["API_KEY"]);
        Assert.Empty(vault.Resolved); // embedded backend: the value never needs the vault
    }

    [Fact]
    public async Task GetSecretsAsync_ReferenceEntries_AreResolvedThroughTheVault()
    {
        var manifest = """
            { "entries": [ { "key": "API_KEY", "source": "Reference", "payload": "kv/production/api_key" } ] }
            """;
        var vault = new RecordingVaultClient();
        vault.Values["kv/production/api_key"] = "resolved-from-vault";
        var client = BuildClient(manifest, vault);

        var secrets = await client.GetSecretsAsync(CancellationToken.None);

        Assert.Equal("resolved-from-vault", secrets["API_KEY"]);
        Assert.Contains("kv/production/api_key", vault.Resolved);
    }

    private static ControlPlaneClient BuildClient(string manifestJson, IVaultClient vault)
    {
        var handler = new StubHandler(manifestJson);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://control-plane.test") };
        var options = new AgentOptions { Environment = "production" };
        return new ControlPlaneClient(http, options, vault, NullLogger<ControlPlaneClient>.Instance);
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private sealed class RecordingVaultClient : IVaultClient
    {
        public Dictionary<string, string> Values { get; } = new();
        public List<string> Resolved { get; } = new();

        public Task<string> ResolveAsync(string reference, CancellationToken ct)
        {
            Resolved.Add(reference);
            return Task.FromResult(Values[reference]);
        }
    }
}
