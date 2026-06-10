using ControlPlane.Features.Alerts.Email;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Features.Auth;

// Sends the password-reset email through the platform-default ZeptoMail sender. Self-hosted
// deployments without email configured simply can't deliver reset links — the request still
// succeeds (no user enumeration) and this logs a clear warning for the operator.
public interface IPasswordResetNotifier
{
    Task SendResetLinkAsync(string email, string resetToken, CancellationToken ct = default);
}

public class PasswordResetNotifier(
    IEnumerable<IEmailSender> emailSenders,
    ZeptoOptions zeptoOptions,
    IConfiguration configuration,
    ILogger<PasswordResetNotifier> logger) : IPasswordResetNotifier
{
    public async Task SendResetLinkAsync(string email, string resetToken, CancellationToken ct = default)
    {
        if (!zeptoOptions.IsConfigured)
        {
            logger.LogWarning(
                "Password reset requested for {Email} but no platform email sender is configured " +
                "(set Zepto:Token and Zepto:FromAddress). No reset link was sent.", email);
            return;
        }

        var baseUrl = configuration["App:BaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogWarning(
                "Password reset requested for {Email} but App:BaseUrl is not configured, so a reset " +
                "link cannot be built. No email was sent.", email);
            return;
        }

        var sender = emailSenders.FirstOrDefault(s => s.Provider == EmailProvider.Zepto);
        if (sender is null)
        {
            logger.LogWarning("No ZeptoMail email sender is registered; password reset email not sent.");
            return;
        }

        var resetLink = $"{baseUrl}/reset-password?token={Uri.EscapeDataString(resetToken)}";

        var message = new EmailMessage(
            FromAddress: zeptoOptions.FromAddress!,
            FromName: zeptoOptions.FromName,
            Recipients: [email],
            Subject: "Reset your Serto password",
            HtmlBody: $"""
                <p>We received a request to reset your Serto password.</p>
                <p><a href="{resetLink}">Reset your password</a></p>
                <p>This link expires in 1 hour. If you didn't request this, you can ignore this email.</p>
                """,
            TextBody: $"""
                We received a request to reset your Serto password.

                Reset your password: {resetLink}

                This link expires in 1 hour. If you didn't request this, you can ignore this email.
                """,
            Smtp: null);

        await sender.SendAsync(message, ct);
    }
}
