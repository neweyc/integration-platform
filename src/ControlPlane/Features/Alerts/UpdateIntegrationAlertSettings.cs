using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Features.Alerts;

public record UpdateIntegrationAlertSettingsCommand(
    Guid TenantId,
    Guid IntegrationId,
    AlertMode Mode,
    bool EmailEnabled,
    string? EmailRecipients,
    bool WebhookEnabled,
    string? WebhookUrl,
    // null = leave unchanged, empty = clear, value = set
    string? WebhookSecret)
    : ICommand<IntegrationAlertSettingsDto>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        new(AuditAction.AlertSettingsUpdated, "IntegrationAlertSettings", IntegrationId.ToString(),
            $"Updated alert settings for integration ({Mode})");
}

public class UpdateIntegrationAlertSettingsHandler(
    IAlertSettingsWriteRepository repository,
    IEncryptionService encryption,
    AlertWebhookOptions webhookOptions)
    : ICommandHandler<UpdateIntegrationAlertSettingsCommand, IntegrationAlertSettingsDto>
{
    public async Task<IntegrationAlertSettingsDto> HandleAsync(
        UpdateIntegrationAlertSettingsCommand command, CancellationToken ct = default)
    {
        Validate(command, webhookOptions);

        if (!await repository.IntegrationExistsAsync(command.TenantId, command.IntegrationId, ct))
            throw new NotFoundException($"Integration '{command.IntegrationId}' not found.");

        var settings = await repository.FindIntegrationSettingsAsync(command.TenantId, command.IntegrationId, ct);
        var isNew = settings is null;

        settings ??= new IntegrationAlertSettings
        {
            TenantId = command.TenantId,
            IntegrationId = command.IntegrationId
        };

        settings.Mode = command.Mode;
        settings.EmailEnabled = command.EmailEnabled;
        settings.EmailRecipients = command.EmailRecipients;
        settings.WebhookEnabled = command.WebhookEnabled;
        settings.WebhookUrl = command.WebhookUrl;
        settings.WebhookEncryptedSecret = command.WebhookSecret switch
        {
            null => settings.WebhookEncryptedSecret,
            "" => null,
            _ => encryption.Encrypt(command.WebhookSecret)
        };
        settings.UpdatedAt = DateTime.UtcNow;

        if (isNew)
            await repository.AddIntegrationSettingsAsync(settings, ct);

        await repository.SaveAsync(ct);

        return IntegrationAlertSettingsDto.From(command.IntegrationId, settings);
    }

    private static void Validate(UpdateIntegrationAlertSettingsCommand command, AlertWebhookOptions webhookOptions)
    {
        // Email recipients / webhook URL only matter for a custom override; inherit and off ignore them.
        if (command.Mode != AlertMode.Custom)
            return;

        if (command.EmailEnabled && string.IsNullOrWhiteSpace(command.EmailRecipients))
            throw new ValidationException("At least one recipient is required when email alerts are enabled.");

        if (command.WebhookEnabled && string.IsNullOrWhiteSpace(command.WebhookUrl))
            throw new ValidationException("A webhook URL is required when webhook alerts are enabled.");

        UpdateTenantAlertSettingsHandler.ValidateWebhookUrl(command.WebhookUrl, webhookOptions);
    }
}
