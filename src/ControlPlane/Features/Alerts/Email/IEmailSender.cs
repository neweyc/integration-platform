namespace ControlPlane.Features.Alerts.Email;

public enum EmailProvider
{
    // ZeptoMail's transactional HTTP API — the platform-global default, configured by the operator.
    Zepto,

    // A tenant's own SMTP server (MailKit) — the per-tenant override for sending from their own domain.
    Smtp
}

// The connection details for a tenant's SMTP server. Only present when the tenant has opted into SMTP.
public record SmtpServer(
    string Host,
    int Port,
    bool UseStartTls,
    string? Username,
    string? Password);

// What to send and who to: the rendered message plus the resolved sender identity and recipients.
public record EmailMessage(
    string FromAddress,
    string? FromName,
    IReadOnlyList<string> Recipients,
    string Subject,
    string HtmlBody,
    string TextBody,
    SmtpServer? Smtp);

// One email provider. Several are registered; the dispatcher selects by Provider. This is the same
// multi-implementation + selection pattern the trigger adapters use.
public interface IEmailSender
{
    EmailProvider Provider { get; }

    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
