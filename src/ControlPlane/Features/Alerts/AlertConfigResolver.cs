using ControlPlane.Features.Alerts.Email;
using Shared.Domain;

namespace ControlPlane.Features.Alerts;

// A ready-to-send email destination: the resolved provider + sender identity + recipient list. Any SMTP
// password is already decrypted.
public record ResolvedEmailTarget(
    EmailProvider Provider,
    string FromAddress,
    string? FromName,
    IReadOnlyList<string> Recipients,
    SmtpServer? Smtp);

// A ready-to-send webhook destination. The signing secret is already decrypted (null if none).
public record ResolvedWebhookTarget(string Url, string? Secret);

public record ResolvedAlertTargets(ResolvedEmailTarget? Email, ResolvedWebhookTarget? Webhook)
{
    public bool HasAny => Email is not null || Webhook is not null;
}

// The platform-global ZeptoMail sender identity, passed to the resolver so email can fall back to it
// when a tenant has not configured their own SMTP server. Null when ZeptoMail is not configured.
public record ZeptoDefaults(string FromAddress, string? FromName);

// Decides where a failure alert actually goes by combining the tenant defaults with any per-integration
// override. Pure apart from the supplied decrypt delegate, so it is straightforward to unit test.
public static class AlertConfigResolver
{
    public static ResolvedAlertTargets Resolve(
        TenantAlertSettings? tenant,
        IntegrationAlertSettings? integration,
        Func<string, string> decrypt,
        ZeptoDefaults? zepto)
    {
        var mode = integration?.Mode ?? AlertMode.Inherit;
        if (mode == AlertMode.Off)
            return new ResolvedAlertTargets(null, null);

        bool emailEnabled;
        string? recipients;
        bool webhookEnabled;
        string? webhookUrl;
        string? webhookEncryptedSecret;

        if (mode == AlertMode.Custom && integration is not null)
        {
            // A custom override stands on its own — it must resolve even with no tenant defaults row
            // (e.g. a custom webhook, or custom email recipients sent via ZeptoMail).
            emailEnabled = integration.EmailEnabled;
            recipients = integration.EmailRecipients;
            webhookEnabled = integration.WebhookEnabled;
            webhookUrl = integration.WebhookUrl;
            webhookEncryptedSecret = integration.WebhookEncryptedSecret;
        }
        else if (tenant is not null)
        {
            emailEnabled = tenant.EmailEnabled;
            recipients = tenant.EmailRecipients;
            webhookEnabled = tenant.WebhookEnabled;
            webhookUrl = tenant.WebhookUrl;
            webhookEncryptedSecret = tenant.WebhookEncryptedSecret;
        }
        else
        {
            // Inheriting, but there are no tenant defaults to inherit.
            return new ResolvedAlertTargets(null, null);
        }

        var email = ResolveEmail(tenant, emailEnabled, recipients, decrypt, zepto);
        var webhook = ResolveWebhook(webhookEnabled, webhookUrl, webhookEncryptedSecret, decrypt);

        return new ResolvedAlertTargets(email, webhook);
    }

    private static ResolvedEmailTarget? ResolveEmail(
        TenantAlertSettings? tenant,
        bool emailEnabled,
        string? recipients,
        Func<string, string> decrypt,
        ZeptoDefaults? zepto)
    {
        if (!emailEnabled)
            return null;

        var recipientList = ParseRecipients(recipients);
        if (recipientList.Count == 0)
            return null;

        // SMTP server is always tenant-level; its presence is what selects the provider. A tenant that
        // configured their own server sends from their domain, otherwise we fall back to ZeptoMail. With
        // no tenant row at all there is no SMTP server, so email can only go via ZeptoMail.
        if (tenant is not null
            && !string.IsNullOrWhiteSpace(tenant.SmtpHost)
            && !string.IsNullOrWhiteSpace(tenant.SmtpFromAddress))
        {
            var password = string.IsNullOrEmpty(tenant.SmtpEncryptedPassword)
                ? null
                : decrypt(tenant.SmtpEncryptedPassword);

            return new ResolvedEmailTarget(
                EmailProvider.Smtp,
                tenant.SmtpFromAddress!,
                tenant.SmtpFromName,
                recipientList,
                new SmtpServer(tenant.SmtpHost!, tenant.SmtpPort, tenant.SmtpUseStartTls, tenant.SmtpUsername, password));
        }

        if (zepto is not null)
            return new ResolvedEmailTarget(EmailProvider.Zepto, zepto.FromAddress, zepto.FromName, recipientList, null);

        // Email is enabled but neither a tenant SMTP server nor ZeptoMail is available — cannot send.
        return null;
    }

    private static ResolvedWebhookTarget? ResolveWebhook(
        bool webhookEnabled,
        string? webhookUrl,
        string? webhookEncryptedSecret,
        Func<string, string> decrypt)
    {
        if (!webhookEnabled || string.IsNullOrWhiteSpace(webhookUrl))
            return null;

        var secret = string.IsNullOrEmpty(webhookEncryptedSecret) ? null : decrypt(webhookEncryptedSecret);
        return new ResolvedWebhookTarget(webhookUrl!, secret);
    }

    // Recipients are stored as a single string; accept commas, semicolons, and newlines as separators.
    public static IReadOnlyList<string> ParseRecipients(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
