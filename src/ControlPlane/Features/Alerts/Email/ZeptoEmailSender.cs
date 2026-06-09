using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ControlPlane.Features.Alerts.Email;

// Sends through ZeptoMail's transactional HTTP API — the platform-global default provider. Used unless
// a tenant has configured their own SMTP server. Credentials come from operator config, never tenant data.
public class ZeptoEmailSender(IHttpClientFactory httpClientFactory, ZeptoOptions options) : IEmailSender
{
    public const string HttpClientName = "zepto";

    public EmailProvider Provider => EmailProvider.Zepto;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!options.IsConfigured)
            throw new InvalidOperationException(
                "ZeptoMail is not configured. Set Zepto:Token and Zepto:FromAddress, or configure a tenant SMTP server.");

        var payload = new ZeptoSendRequest
        {
            From = new ZeptoAddress(message.FromAddress, message.FromName),
            To = message.Recipients
                .Select(r => new ZeptoRecipient(new ZeptoAddress(r, null)))
                .ToList(),
            Subject = message.Subject,
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody
        };

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, options.BaseUrl)
        {
            Content = JsonContent.Create(payload)
        };
        // ZeptoMail expects the token prefixed with this literal scheme, not a standard bearer token.
        request.Headers.Authorization = new AuthenticationHeaderValue("Zoho-enczapikey", options.Token);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"ZeptoMail send failed ({(int)response.StatusCode} {response.ReasonPhrase}): {body}");
        }
    }
}
