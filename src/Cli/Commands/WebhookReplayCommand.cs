using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Cli.Commands;

public sealed class WebhookReplayCommand : AsyncCommand<WebhookReplayCommand.Settings>
{
    public const string SignatureHeader = "X-Integration-Signature";
    public const string TimestampHeader = "X-Integration-Timestamp";
    public const string DeliveryHeader = "X-Integration-Delivery";

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<WebhookUrl>")]
        [Description("The webhook URL to replay against, such as http://localhost:5000/webhooks/acme/orders/hook.")]
        public string WebhookUrl { get; init; } = string.Empty;

        [CommandOption("-s|--secret")]
        [Description("Webhook signing secret. Defaults to SERTO_WEBHOOK_SECRET.")]
        public string? Secret { get; init; }

        [CommandOption("-p|--payload")]
        [Description("Raw JSON/string payload to send.")]
        public string? Payload { get; init; }

        [CommandOption("-f|--payload-file")]
        [Description("Path to a file containing the raw payload to send.")]
        public string? PayloadFile { get; init; }

        [CommandOption("-d|--delivery-id")]
        [Description("Delivery id for idempotency. Defaults to a generated replay id.")]
        public string? DeliveryId { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        var secret = ResolveSecret(settings.Secret, Environment.GetEnvironmentVariable("SERTO_WEBHOOK_SECRET"));
        if (secret is null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Missing webhook secret. Pass --secret or set SERTO_WEBHOOK_SECRET.");
            return 1;
        }

        var payload = await ResolvePayloadAsync(settings.Payload, settings.PayloadFile, ct);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = CreateSignature(secret, timestamp, Encoding.UTF8.GetBytes(payload));
        var deliveryId = ResolveDeliveryId(settings.DeliveryId);

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.WebhookUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(SignatureHeader, signature);
        request.Headers.Add(TimestampHeader, timestamp);
        request.Headers.Add(DeliveryHeader, deliveryId);

        AnsiConsole.MarkupLine($"[blue]Replaying webhook:[/] [green]{Markup.Escape(settings.WebhookUrl)}[/]");
        AnsiConsole.MarkupLine($"[blue]Delivery id:[/] [green]{Markup.Escape(deliveryId)}[/]");

        var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]Replay failed:[/] {(int)response.StatusCode} {Markup.Escape(response.ReasonPhrase ?? response.StatusCode.ToString())}");
            if (!string.IsNullOrWhiteSpace(responseBody))
                AnsiConsole.WriteLine(responseBody);
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Replay accepted:[/] {(int)response.StatusCode} {Markup.Escape(response.ReasonPhrase ?? response.StatusCode.ToString())}");
        if (!string.IsNullOrWhiteSpace(responseBody))
            AnsiConsole.WriteLine(responseBody);

        return 0;
    }

    public static string? ResolveSecret(string? explicitSecret, string? environmentSecret)
    {
        if (!string.IsNullOrWhiteSpace(explicitSecret))
            return explicitSecret.Trim();

        if (!string.IsNullOrWhiteSpace(environmentSecret))
            return environmentSecret.Trim();

        return null;
    }

    public static string ResolveDeliveryId(string? explicitDeliveryId) =>
        string.IsNullOrWhiteSpace(explicitDeliveryId)
            ? "replay-" + Guid.NewGuid().ToString("N")
            : explicitDeliveryId.Trim();

    public static async Task<string> ResolvePayloadAsync(string? payload, string? payloadFile, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(payload) && !string.IsNullOrWhiteSpace(payloadFile))
            throw new InvalidOperationException("Use either --payload or --payload-file, not both.");

        if (!string.IsNullOrWhiteSpace(payloadFile))
        {
            if (!File.Exists(payloadFile))
                throw new FileNotFoundException("Payload file not found.", payloadFile);

            return await File.ReadAllTextAsync(payloadFile, ct);
        }

        return payload ?? "{}";
    }

    public static string CreateSignature(string secret, string timestamp, byte[] bodyBytes)
    {
        var prefix = Encoding.UTF8.GetBytes($"{timestamp}.");
        var signedPayload = new byte[prefix.Length + bodyBytes.Length];
        Buffer.BlockCopy(prefix, 0, signedPayload, 0, prefix.Length);
        Buffer.BlockCopy(bodyBytes, 0, signedPayload, prefix.Length, bodyBytes.Length);

        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signedPayload);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
