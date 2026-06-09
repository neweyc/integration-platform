using System.Net;
using System.Net.Sockets;

namespace ControlPlane.Features.Alerts;

public class AlertWebhookOptions
{
    // When false (the default), webhook alert targets that resolve to loopback, private, link-local,
    // carrier-grade-NAT, or cloud-metadata addresses are blocked to prevent server-side request forgery.
    // Self-hosted operators that deliberately post alerts to internal endpoints can set this true.
    public bool AllowPrivateNetworkTargets { get; set; }
}

// Defends the webhook alert channel against SSRF. The authoritative check is at connect time (see
// CreateGuardedConnectCallback), which validates the actual IP the socket connects to and therefore
// also defeats DNS-rebinding. A cheaper literal-IP check is exposed for fast feedback when settings
// are saved.
public static class OutboundWebhookGuard
{
    public static bool IsValidHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // True when the URL's host is an IP literal in a disallowed range. Hostnames are not resolved here
    // (that DNS work happens at connect time); this only catches the obvious literal cases at save time.
    public static bool IsLiteralPrivateTarget(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && IPAddress.TryParse(uri.Host, out var ip)
        && IsDisallowed(ip);

    public static bool IsDisallowed(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return true;

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 0                                   // 0.0.0.0/8 "this host"
                || bytes[0] == 10                                  // 10.0.0.0/8 private
                || (bytes[0] == 100 && (bytes[1] & 0xC0) == 64)    // 100.64.0.0/10 carrier-grade NAT
                || bytes[0] == 127                                 // loopback (also caught above)
                || (bytes[0] == 169 && bytes[1] == 254)            // 169.254.0.0/16 link-local + metadata
                || (bytes[0] == 172 && (bytes[1] & 0xF0) == 16)    // 172.16.0.0/12 private
                || (bytes[0] == 192 && bytes[1] == 168)            // 192.168.0.0/16 private
                || bytes[0] >= 224;                                // 224.0.0.0/3 multicast + reserved
        }

        // IPv6
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            return true;
        if (IPAddress.IPv6Any.Equals(address))
            return true;
        return (bytes[0] & 0xFE) == 0xFC;                          // fc00::/7 unique-local
    }

    // A SocketsHttpHandler connect callback that resolves the host and connects only to addresses in
    // allowed ranges, so a webhook can never be used to reach internal/metadata endpoints.
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> CreateGuardedConnectCallback() =>
        async (context, ct) =>
        {
            var endpoint = context.DnsEndPoint;
            var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, ct);
            var allowed = addresses.Where(a => !IsDisallowed(a)).ToArray();

            if (allowed.Length == 0)
                throw new IOException(
                    $"Webhook host '{endpoint.Host}' resolves only to private or reserved addresses, which are not allowed.");

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, endpoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
}
