using ControlPlane.Features.Alerts.Email;
using ControlPlane.Infrastructure;

namespace ControlPlane.Features.Alerts;

// The result of attempting to deliver one alert across its configured channels. Used to log dispatch
// outcomes and to report test-send results back to the UI.
public record AlertSendOutcome(
    bool EmailAttempted,
    bool EmailSucceeded,
    string? EmailError,
    bool WebhookAttempted,
    bool WebhookSucceeded,
    string? WebhookError)
{
    public bool AnyAttempted => EmailAttempted || WebhookAttempted;

    public static readonly AlertSendOutcome None = new(false, false, null, false, false, null);
}

// Resolves where an alert should go (tenant defaults + per-integration override) and delivers it across
// each enabled channel. Shared by failure alerts, unroutable-work alerts, and the test-send command so
// all behave identically. A failure on one channel never prevents the other from being attempted.
public interface IAlertNotifier
{
    Task<AlertSendOutcome> SendAsync(FailedExecutionAlert alert, CancellationToken ct = default);
    Task<AlertSendOutcome> SendAsync(UnroutableIntegrationAlert alert, CancellationToken ct = default);
}

public class AlertNotifier(
    IAlertSettingsReadRepository repository,
    IEncryptionService encryption,
    IEnumerable<IEmailSender> emailSenders,
    IWebhookAlertSender webhookSender,
    ZeptoOptions zepto,
    ILogger<AlertNotifier> logger) : IAlertNotifier
{
    public Task<AlertSendOutcome> SendAsync(FailedExecutionAlert alert, CancellationToken ct = default) =>
        SendCoreAsync(
            alert.TenantId,
            alert.IntegrationId,
            AlertMessageFormatter.Subject(alert),
            AlertMessageFormatter.HtmlBody(alert),
            AlertMessageFormatter.TextBody(alert),
            AlertMessageFormatter.JsonPayload(alert),
            logContext: $"execution {alert.ExecutionId}",
            ct);

    public Task<AlertSendOutcome> SendAsync(UnroutableIntegrationAlert alert, CancellationToken ct = default) =>
        SendCoreAsync(
            alert.TenantId,
            alert.IntegrationId,
            UnroutableAlertFormatter.Subject(alert),
            UnroutableAlertFormatter.HtmlBody(alert),
            UnroutableAlertFormatter.TextBody(alert),
            UnroutableAlertFormatter.JsonPayload(alert),
            logContext: $"unroutable integration {alert.Slug}",
            ct);

    // Resolves channels once and renders the same content to each. Content is pre-rendered by the
    // caller's formatter, so this core knows nothing about a specific alert kind.
    private async Task<AlertSendOutcome> SendCoreAsync(
        Guid tenantId,
        Guid integrationId,
        string subject,
        string htmlBody,
        string textBody,
        string jsonPayload,
        string logContext,
        CancellationToken ct)
    {
        var tenant = await repository.GetTenantSettingsAsync(tenantId, ct);

        // A tenant-level test passes Guid.Empty so it exercises only the tenant defaults.
        var integration = integrationId == Guid.Empty
            ? null
            : await repository.GetIntegrationSettingsAsync(tenantId, integrationId, ct);

        var zeptoDefaults = zepto.IsConfigured ? new ZeptoDefaults(zepto.FromAddress!, zepto.FromName) : null;
        var targets = AlertConfigResolver.Resolve(tenant, integration, encryption.Decrypt, zeptoDefaults);

        if (!targets.HasAny)
            return AlertSendOutcome.None;

        var (emailAttempted, emailSucceeded, emailError) =
            await TrySendEmailAsync(targets.Email, subject, htmlBody, textBody, logContext, ct);
        var (webhookAttempted, webhookSucceeded, webhookError) =
            await TrySendWebhookAsync(targets.Webhook, jsonPayload, logContext, ct);

        return new AlertSendOutcome(
            emailAttempted, emailSucceeded, emailError,
            webhookAttempted, webhookSucceeded, webhookError);
    }

    private async Task<(bool Attempted, bool Succeeded, string? Error)> TrySendEmailAsync(
        ResolvedEmailTarget? target, string subject, string htmlBody, string textBody, string logContext, CancellationToken ct)
    {
        if (target is null)
            return (false, false, null);

        try
        {
            var sender = emailSenders.FirstOrDefault(s => s.Provider == target.Provider)
                ?? throw new InvalidOperationException($"No email sender registered for provider '{target.Provider}'.");

            await sender.SendAsync(
                new EmailMessage(
                    target.FromAddress,
                    target.FromName,
                    target.Recipients,
                    subject,
                    htmlBody,
                    textBody,
                    target.Smtp),
                ct);

            return (true, true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Email alert for {Context} failed", logContext);
            return (true, false, ex.Message);
        }
    }

    private async Task<(bool Attempted, bool Succeeded, string? Error)> TrySendWebhookAsync(
        ResolvedWebhookTarget? target, string jsonPayload, string logContext, CancellationToken ct)
    {
        if (target is null)
            return (false, false, null);

        try
        {
            await webhookSender.SendAsync(target, jsonPayload, ct);
            return (true, true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook alert for {Context} failed", logContext);
            return (true, false, ex.Message);
        }
    }
}
