using ControlPlane.Features.Alerts.Email;
using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Features.Alerts;

public record UpdateTenantAlertSettingsCommand(
    Guid TenantId,
    bool EmailEnabled,
    string? EmailRecipients,
    string? SmtpHost,
    int SmtpPort,
    bool SmtpUseStartTls,
    string? SmtpUsername,
    // null = leave unchanged, empty = clear, value = set
    string? SmtpPassword,
    string? SmtpFromAddress,
    string? SmtpFromName,
    bool WebhookEnabled,
    string? WebhookUrl,
    // null = leave unchanged, empty = clear, value = set
    string? WebhookSecret)
    : ICommand<TenantAlertSettingsDto>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        new(AuditAction.AlertSettingsUpdated, "AlertSettings", TenantId.ToString(),
            "Updated tenant alert settings");
}

public class UpdateTenantAlertSettingsHandler(
    IAlertSettingsWriteRepository repository,
    IEncryptionService encryption,
    ZeptoOptions zepto,
    AlertWebhookOptions webhookOptions)
    : ICommandHandler<UpdateTenantAlertSettingsCommand, TenantAlertSettingsDto>
{
    public async Task<TenantAlertSettingsDto> HandleAsync(
        UpdateTenantAlertSettingsCommand command, CancellationToken ct = default)
    {
        Validate(command, webhookOptions);

        var settings = await repository.FindTenantSettingsAsync(command.TenantId, ct);
        var isNew = settings is null;

        settings ??= new TenantAlertSettings { TenantId = command.TenantId };

        settings.EmailEnabled = command.EmailEnabled;
        settings.EmailRecipients = command.EmailRecipients;
        settings.SmtpHost = command.SmtpHost;
        settings.SmtpPort = command.SmtpPort;
        settings.SmtpUseStartTls = command.SmtpUseStartTls;
        settings.SmtpUsername = command.SmtpUsername;
        settings.SmtpFromAddress = command.SmtpFromAddress;
        settings.SmtpFromName = command.SmtpFromName;
        settings.WebhookEnabled = command.WebhookEnabled;
        settings.WebhookUrl = command.WebhookUrl;

        settings.SmtpEncryptedPassword = ApplySecret(command.SmtpPassword, settings.SmtpEncryptedPassword);
        settings.WebhookEncryptedSecret = ApplySecret(command.WebhookSecret, settings.WebhookEncryptedSecret);
        settings.UpdatedAt = DateTime.UtcNow;

        if (isNew)
            await repository.AddTenantSettingsAsync(settings, ct);

        await repository.SaveAsync(ct);

        return TenantAlertSettingsDto.From(settings, zepto);
    }

    // Encrypts a new secret, clears it, or leaves the stored value untouched per the input convention.
    private string? ApplySecret(string? incoming, string? existing) => incoming switch
    {
        null => existing,                 // not provided — keep what's stored
        "" => null,                       // explicitly cleared
        _ => encryption.Encrypt(incoming) // new value
    };

    private static void Validate(UpdateTenantAlertSettingsCommand command, AlertWebhookOptions webhookOptions)
    {
        if (command.SmtpPort is < 1 or > 65535)
            throw new ValidationException("SMTP port must be between 1 and 65535.");

        // If a tenant configures an SMTP server, it needs both a host and a verified From address.
        if (!string.IsNullOrWhiteSpace(command.SmtpHost) && string.IsNullOrWhiteSpace(command.SmtpFromAddress))
            throw new ValidationException("A From address is required when an SMTP server is configured.");

        if (command.EmailEnabled
            && string.IsNullOrWhiteSpace(command.EmailRecipients))
            throw new ValidationException("At least one recipient is required when email alerts are enabled.");

        if (command.WebhookEnabled && string.IsNullOrWhiteSpace(command.WebhookUrl))
            throw new ValidationException("A webhook URL is required when webhook alerts are enabled.");

        ValidateWebhookUrl(command.WebhookUrl, webhookOptions);
    }

    // Shared with the per-integration handler: scheme check plus an SSRF literal-IP check unless the
    // operator allows private targets. (DNS-resolved hosts are also checked at connect time.)
    public static void ValidateWebhookUrl(string? url, AlertWebhookOptions webhookOptions)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (!OutboundWebhookGuard.IsValidHttpUrl(url))
            throw new ValidationException("Webhook URL must be a valid http(s) URL.");

        if (!webhookOptions.AllowPrivateNetworkTargets && OutboundWebhookGuard.IsLiteralPrivateTarget(url))
            throw new ValidationException("Webhook URL points to a private or reserved address, which is not allowed.");
    }
}
