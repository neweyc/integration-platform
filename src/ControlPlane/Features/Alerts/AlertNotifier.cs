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

// Resolves where a failure alert should go (tenant defaults + per-integration override) and delivers it
// across each enabled channel. Shared by the background dispatcher and the test-send command so both
// behave identically. A failure on one channel never prevents the other from being attempted.
public interface IAlertNotifier
{
    Task<AlertSendOutcome> SendAsync(FailedExecutionAlert alert, CancellationToken ct = default);
}

public class AlertNotifier(
    IAlertSettingsReadRepository repository,
    IEncryptionService encryption,
    IEnumerable<IEmailSender> emailSenders,
    IWebhookAlertSender webhookSender,
    ZeptoOptions zepto,
    ILogger<AlertNotifier> logger) : IAlertNotifier
{
    public async Task<AlertSendOutcome> SendAsync(FailedExecutionAlert alert, CancellationToken ct = default)
    {
        var tenant = await repository.GetTenantSettingsAsync(alert.TenantId, ct);

        // A tenant-level test passes Guid.Empty so it exercises only the tenant defaults.
        var integration = alert.IntegrationId == Guid.Empty
            ? null
            : await repository.GetIntegrationSettingsAsync(alert.TenantId, alert.IntegrationId, ct);

        var zeptoDefaults = zepto.IsConfigured ? new ZeptoDefaults(zepto.FromAddress!, zepto.FromName) : null;
        var targets = AlertConfigResolver.Resolve(tenant, integration, encryption.Decrypt, zeptoDefaults);

        if (!targets.HasAny)
            return AlertSendOutcome.None;

        var (emailAttempted, emailSucceeded, emailError) = await TrySendEmailAsync(targets.Email, alert, ct);
        var (webhookAttempted, webhookSucceeded, webhookError) = await TrySendWebhookAsync(targets.Webhook, alert, ct);

        return new AlertSendOutcome(
            emailAttempted, emailSucceeded, emailError,
            webhookAttempted, webhookSucceeded, webhookError);
    }

    private async Task<(bool Attempted, bool Succeeded, string? Error)> TrySendEmailAsync(
        ResolvedEmailTarget? target, FailedExecutionAlert alert, CancellationToken ct)
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
                    AlertMessageFormatter.Subject(alert),
                    AlertMessageFormatter.HtmlBody(alert),
                    AlertMessageFormatter.TextBody(alert),
                    target.Smtp),
                ct);

            return (true, true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Email alert for execution {ExecutionId} failed", alert.ExecutionId);
            return (true, false, ex.Message);
        }
    }

    private async Task<(bool Attempted, bool Succeeded, string? Error)> TrySendWebhookAsync(
        ResolvedWebhookTarget? target, FailedExecutionAlert alert, CancellationToken ct)
    {
        if (target is null)
            return (false, false, null);

        try
        {
            await webhookSender.SendAsync(target, AlertMessageFormatter.JsonPayload(alert), ct);
            return (true, true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook alert for execution {ExecutionId} failed", alert.ExecutionId);
            return (true, false, ex.Message);
        }
    }
}
