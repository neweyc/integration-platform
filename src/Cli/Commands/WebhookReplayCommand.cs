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

    // Mirrors WebhookHeaders.ToleranceSeconds on the control plane. The CLI implements the webhook
    // wire contract independently (it cannot reference the control plane), so this constant — like the
    // header names above — is kept in step with the server and locked by tests.
    public const int ToleranceSeconds = 300;

    // The signing secret used by `--local` when none is supplied. In loopback the CLI both signs and
    // verifies, so the value only has to be self-consistent — it never leaves the machine.
    public const string DefaultLocalSecret = "local-replay-secret";

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[WebhookUrl]")]
        [Description("The webhook URL to replay against, such as http://localhost:5000/webhooks/acme/orders/hook. Omit when using --local.")]
        public string? WebhookUrl { get; init; }

        [CommandOption("-l|--local")]
        [Description("Run the integration locally with the signed payload instead of POSTing to a control plane.")]
        public bool Local { get; init; }

        [CommandOption("-c|--class")]
        [Description("Integration class to run in --local mode. Defaults to the first integration found.")]
        public string? ClassName { get; init; }

        [CommandOption("--secrets")]
        [Description("Path to a JSON file of integration secrets, used when running the integration in --local mode.")]
        public string? SecretsPath { get; init; }

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
        var payload = await ResolvePayloadAsync(settings.Payload, settings.PayloadFile, ct);

        if (settings.Local)
            return await RunLocalAsync(settings, payload, ct);

        if (string.IsNullOrWhiteSpace(settings.WebhookUrl))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] A webhook URL is required. Pass one, or use --local to run the integration without a control plane.");
            return 1;
        }

        var secret = ResolveSecret(settings.Secret, Environment.GetEnvironmentVariable("SERTO_WEBHOOK_SECRET"));
        if (secret is null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Missing webhook secret. Pass --secret or set SERTO_WEBHOOK_SECRET.");
            return 1;
        }

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

        AnsiConsole.MarkupLine($"[blue]Replaying webhook:[/] [green]{Markup.Escape(settings.WebhookUrl!)}[/]");
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

    // Loopback replay: sign the payload, validate the signed delivery exactly as the control plane would
    // (signature + freshness), then run the integration locally with the payload. No control plane, no
    // network, no external sender — the whole webhook path is exercised offline.
    private static async Task<int> RunLocalAsync(Settings settings, string payload, CancellationToken ct)
    {
        // In loopback the CLI both signs and verifies, so any self-consistent secret works; default one
        // so the developer is not forced to invent a value just to test their handler.
        var secret = ResolveSecret(settings.Secret, Environment.GetEnvironmentVariable("SERTO_WEBHOOK_SECRET"))
            ?? DefaultLocalSecret;

        var bodyBytes = Encoding.UTF8.GetBytes(payload);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = CreateSignature(secret, timestamp, bodyBytes);
        var deliveryId = ResolveDeliveryId(settings.DeliveryId);

        AnsiConsole.MarkupLine("[blue]Local webhook replay[/] [grey](no control plane)[/]");
        AnsiConsole.MarkupLine($"[blue]Delivery id:[/] [green]{Markup.Escape(deliveryId)}[/]");
        AnsiConsole.MarkupLine($"[grey]{SignatureHeader}: {Markup.Escape(signature)}[/]");
        AnsiConsole.MarkupLine($"[grey]{TimestampHeader}: {Markup.Escape(timestamp)}[/]");

        // Prove the signed delivery the CLI produces is one the control plane would actually accept.
        if (!VerifySignature(secret, timestamp, bodyBytes, signature))
        {
            AnsiConsole.MarkupLine("[red]Signature self-check failed.[/] The signed payload would be rejected by the control plane.");
            return 1;
        }

        if (!IsTimestampFresh(timestamp, DateTimeOffset.UtcNow))
        {
            AnsiConsole.MarkupLine("[red]Timestamp is outside the freshness window.[/] Check the system clock.");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]✓ Signature verified and timestamp fresh[/] — the control plane would accept this delivery.");

        // Run the integration with the payload through the same harness as `serto test`.
        return await new TestCommand().RunAsync(new TestCommand.Settings
        {
            ClassName = settings.ClassName,
            SecretsPath = settings.SecretsPath,
            Payload = payload
        }, ct);
    }

    // Recomputes the expected signature and compares it to the supplied header in constant time — the
    // same check (algorithm, sha256= prefix, lowercase hex, fixed-time compare) the control plane runs
    // in DeliverWebhook.VerifySignature, but against a plaintext secret rather than a stored encrypted one.
    public static bool VerifySignature(string secret, string timestamp, byte[] bodyBytes, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(signatureHeader))
            return false;

        if (!signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            return false;

        var received = signatureHeader["sha256=".Length..].ToLowerInvariant();
        if (received.Length == 0)
            return false;

        var expected = CreateSignature(secret, timestamp, bodyBytes)["sha256=".Length..];
        if (received.Length != expected.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(received));
    }

    // Mirrors DeliverWebhook.IsTimestampFresh: an unparseable timestamp is stale, otherwise it must be
    // within the tolerance window of now.
    public static bool IsTimestampFresh(string? timestamp, DateTimeOffset now)
    {
        if (!long.TryParse(timestamp, out var seconds))
            return false;

        return Math.Abs(now.ToUnixTimeSeconds() - seconds) <= ToleranceSeconds;
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
