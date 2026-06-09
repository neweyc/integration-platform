using ControlPlane.Features.Alerts.Email;
using Shared.Domain;

namespace ControlPlane.Features.Alerts;

// Tenant alert settings as returned to the UI. Secrets are never returned — only whether one is set —
// and the platform ZeptoMail details are surfaced read-only so the UI can explain the email default.
public record TenantAlertSettingsDto(
    bool EmailEnabled,
    string? EmailRecipients,
    string? SmtpHost,
    int SmtpPort,
    bool SmtpUseStartTls,
    string? SmtpUsername,
    bool SmtpPasswordSet,
    string? SmtpFromAddress,
    string? SmtpFromName,
    bool WebhookEnabled,
    string? WebhookUrl,
    bool WebhookSecretSet,
    bool ZeptoConfigured,
    string? ZeptoFromAddress)
{
    public static TenantAlertSettingsDto From(TenantAlertSettings? settings, ZeptoOptions zepto) =>
        new(
            EmailEnabled: settings?.EmailEnabled ?? false,
            EmailRecipients: settings?.EmailRecipients,
            SmtpHost: settings?.SmtpHost,
            SmtpPort: settings?.SmtpPort ?? 587,
            SmtpUseStartTls: settings?.SmtpUseStartTls ?? true,
            SmtpUsername: settings?.SmtpUsername,
            SmtpPasswordSet: !string.IsNullOrEmpty(settings?.SmtpEncryptedPassword),
            SmtpFromAddress: settings?.SmtpFromAddress,
            SmtpFromName: settings?.SmtpFromName,
            WebhookEnabled: settings?.WebhookEnabled ?? false,
            WebhookUrl: settings?.WebhookUrl,
            WebhookSecretSet: !string.IsNullOrEmpty(settings?.WebhookEncryptedSecret),
            ZeptoConfigured: zepto.IsConfigured,
            ZeptoFromAddress: zepto.FromAddress);
}

// Per-integration override as returned to the UI. Webhook secret is masked the same way.
public record IntegrationAlertSettingsDto(
    Guid IntegrationId,
    AlertMode Mode,
    bool EmailEnabled,
    string? EmailRecipients,
    bool WebhookEnabled,
    string? WebhookUrl,
    bool WebhookSecretSet)
{
    public static IntegrationAlertSettingsDto From(Guid integrationId, IntegrationAlertSettings? settings) =>
        new(
            IntegrationId: integrationId,
            Mode: settings?.Mode ?? AlertMode.Inherit,
            EmailEnabled: settings?.EmailEnabled ?? false,
            EmailRecipients: settings?.EmailRecipients,
            WebhookEnabled: settings?.WebhookEnabled ?? false,
            WebhookUrl: settings?.WebhookUrl,
            WebhookSecretSet: !string.IsNullOrEmpty(settings?.WebhookEncryptedSecret));
}

// Request bodies. Secret fields follow a "null = leave unchanged, empty = clear, value = set" convention
// so a secret never has to be re-entered just to toggle an unrelated field.
public record UpdateTenantAlertSettingsRequest(
    bool EmailEnabled,
    string? EmailRecipients,
    string? SmtpHost,
    int? SmtpPort,
    bool? SmtpUseStartTls,
    string? SmtpUsername,
    string? SmtpPassword,
    string? SmtpFromAddress,
    string? SmtpFromName,
    bool WebhookEnabled,
    string? WebhookUrl,
    string? WebhookSecret);

public record UpdateIntegrationAlertSettingsRequest(
    AlertMode Mode,
    bool EmailEnabled,
    string? EmailRecipients,
    bool WebhookEnabled,
    string? WebhookUrl,
    string? WebhookSecret);
