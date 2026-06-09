using System.Security.Cryptography;
using System.Text;

namespace ControlPlane.Features.Alerts;

// Posts a JSON alert payload to a configured URL (Slack/Teams/Discord/PagerDuty incoming webhooks, or a
// custom endpoint). When a secret is set, signs the body with HMAC-SHA256 so the receiver can verify it.
public interface IWebhookAlertSender
{
    Task SendAsync(ResolvedWebhookTarget target, string jsonPayload, CancellationToken ct = default);
}

public class WebhookAlertSender(IHttpClientFactory httpClientFactory) : IWebhookAlertSender
{
    public const string HttpClientName = "alert-webhook";
    public const string SignatureHeader = "X-Serto-Signature";

    public async Task SendAsync(ResolvedWebhookTarget target, string jsonPayload, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, target.Url)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrEmpty(target.Secret))
            request.Headers.TryAddWithoutValidation(SignatureHeader, $"sha256={Sign(target.Secret, jsonPayload)}");

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private static string Sign(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }
}
