using System.Text.Json.Serialization;

namespace ControlPlane.Features.Alerts.Email;

// Maps to ZeptoMail's /email request body. Property names follow ZeptoMail's wire format exactly.
public record ZeptoSendRequest
{
    [JsonPropertyName("from")]
    public required ZeptoAddress From { get; init; }

    [JsonPropertyName("to")]
    public required IReadOnlyList<ZeptoRecipient> To { get; init; }

    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    [JsonPropertyName("htmlbody")]
    public required string HtmlBody { get; init; }

    [JsonPropertyName("textbody")]
    public required string TextBody { get; init; }
}

public record ZeptoRecipient(
    [property: JsonPropertyName("email_address")] ZeptoAddress EmailAddress);

public record ZeptoAddress(
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("name")] string? Name);
