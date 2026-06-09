using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ControlPlane.Features.Alerts.Email;

// Sends through a tenant's own SMTP server using MailKit. Selected when the tenant has configured an
// SMTP host (so alerts come from their domain / infrastructure rather than the platform default).
public class SmtpEmailSender : IEmailSender
{
    public EmailProvider Provider => EmailProvider.Smtp;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (message.Smtp is null)
            throw new InvalidOperationException("SMTP sender requires SMTP server details.");

        var server = message.Smtp;

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(message.FromName ?? string.Empty, message.FromAddress));
        foreach (var recipient in message.Recipients)
            mime.To.Add(MailboxAddress.Parse(recipient));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        using var smtp = new SmtpClient();
        var socketOptions = server.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
        await smtp.ConnectAsync(server.Host, server.Port, socketOptions, ct);

        // Anonymous relays exist; only authenticate when a username was supplied.
        if (!string.IsNullOrEmpty(server.Username))
            await smtp.AuthenticateAsync(server.Username, server.Password ?? string.Empty, ct);

        await smtp.SendAsync(mime, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}
