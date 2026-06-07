using System.Security.Cryptography;
using System.Text;
using Cli.Commands;

namespace Cli.Tests;

public class WebhookReplayCommandTests
{
    [Fact]
    public void CreateSignature_SignsTimestampDotBodyWithSha256Prefix()
    {
        var body = Encoding.UTF8.GetBytes("""{"orderId":123}""");
        var signature = WebhookReplayCommand.CreateSignature("whs_secret", "1790000000", body);

        var expectedPayload = Encoding.UTF8.GetBytes("1790000000.").Concat(body).ToArray();
        var expectedHash = HMACSHA256.HashData(Encoding.UTF8.GetBytes("whs_secret"), expectedPayload);

        Assert.Equal("sha256=" + Convert.ToHexString(expectedHash).ToLowerInvariant(), signature);
    }

    [Fact]
    public void ResolveSecret_PrefersExplicitSecret()
    {
        var secret = WebhookReplayCommand.ResolveSecret(" whs_explicit ", "whs_env");

        Assert.Equal("whs_explicit", secret);
    }

    [Fact]
    public void ResolveSecret_UsesEnvironmentSecret()
    {
        var secret = WebhookReplayCommand.ResolveSecret(null, " whs_env ");

        Assert.Equal("whs_env", secret);
    }

    [Fact]
    public void ResolveDeliveryId_GeneratesReplayIdWhenMissing()
    {
        var deliveryId = WebhookReplayCommand.ResolveDeliveryId(null);

        Assert.StartsWith("replay-", deliveryId);
    }

    [Fact]
    public async Task ResolvePayloadAsync_DefaultsToEmptyJson()
    {
        var payload = await WebhookReplayCommand.ResolvePayloadAsync(null, null);

        Assert.Equal("{}", payload);
    }

    [Fact]
    public async Task ResolvePayloadAsync_LoadsPayloadFile()
    {
        using var file = new TemporaryFile("""{"event":"created"}""");

        var payload = await WebhookReplayCommand.ResolvePayloadAsync(null, file.Path);

        Assert.Equal("""{"event":"created"}""", payload);
    }

    [Fact]
    public async Task ResolvePayloadAsync_RejectsInlineAndFilePayload()
    {
        using var file = new TemporaryFile("{}");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WebhookReplayCommand.ResolvePayloadAsync("{}", file.Path));
    }

    private sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile(string contents)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(Path, contents);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
    }
}
