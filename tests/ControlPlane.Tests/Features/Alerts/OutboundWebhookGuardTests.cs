using System.Net;
using ControlPlane.Features.Alerts;

namespace ControlPlane.Tests.Features.Alerts;

public class OutboundWebhookGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]       // loopback
    [InlineData("10.0.0.5")]        // private
    [InlineData("172.16.4.4")]      // private
    [InlineData("172.31.255.255")]  // private (top of /12)
    [InlineData("192.168.1.1")]     // private
    [InlineData("169.254.169.254")] // link-local + cloud metadata
    [InlineData("100.64.0.1")]      // carrier-grade NAT
    [InlineData("0.0.0.0")]         // unspecified
    [InlineData("224.0.0.1")]       // multicast
    [InlineData("::1")]             // IPv6 loopback
    [InlineData("fe80::1")]         // IPv6 link-local
    [InlineData("fc00::1")]         // IPv6 unique-local
    public void IsDisallowed_TrueForPrivateAndReserved(string ip)
    {
        Assert.True(OutboundWebhookGuard.IsDisallowed(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.32.0.1")]   // just outside the 172.16/12 private block
    [InlineData("93.184.216.34")]
    [InlineData("2606:4700:4700::1111")] // public IPv6 (Cloudflare)
    public void IsDisallowed_FalseForPublic(string ip)
    {
        Assert.False(OutboundWebhookGuard.IsDisallowed(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/", true)]
    [InlineData("https://10.0.0.1/hook", true)]
    [InlineData("https://hooks.slack.com/services/x", false)] // hostname — not checked here, allowed
    [InlineData("https://93.184.216.34/hook", false)]
    public void IsLiteralPrivateTarget_FlagsLiteralPrivateHostsOnly(string url, bool expected)
    {
        Assert.Equal(expected, OutboundWebhookGuard.IsLiteralPrivateTarget(url));
    }

    [Theory]
    [InlineData("https://hooks.slack.com/x", true)]
    [InlineData("http://example.com/x", true)]
    [InlineData("ftp://example.com/x", false)]
    [InlineData("not-a-url", false)]
    public void IsValidHttpUrl_RequiresHttpScheme(string url, bool expected)
    {
        Assert.Equal(expected, OutboundWebhookGuard.IsValidHttpUrl(url));
    }
}
