namespace ControlPlane.Features.Alerts.Email;

// Platform-global ZeptoMail configuration, supplied by the operator via the "Zepto" config section /
// environment variables. Shared by every tenant; the API token never touches tenant data.
public class ZeptoOptions
{
    // ZeptoMail "Send Mail" token. Sent as the Authorization header value "Zoho-enczapikey <token>".
    public string? Token { get; set; }

    // The verified sender all platform-sent alerts come from. Must be a verified domain/address in the
    // operator's ZeptoMail account.
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }

    // ZeptoMail's transactional email endpoint. Overridable for the EU data-center or testing.
    public string BaseUrl { get; set; } = "https://api.zeptomail.com/v1.1/email";

    // ZeptoMail can only send once a token and a verified sender are configured.
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(FromAddress);
}
