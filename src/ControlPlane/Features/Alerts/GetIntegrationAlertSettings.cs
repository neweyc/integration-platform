using ControlPlane.Infrastructure;

namespace ControlPlane.Features.Alerts;

public record GetIntegrationAlertSettingsCommand(Guid TenantId, Guid IntegrationId)
    : ICommand<IntegrationAlertSettingsDto>;

public class GetIntegrationAlertSettingsHandler(IAlertSettingsReadRepository repository)
    : ICommandHandler<GetIntegrationAlertSettingsCommand, IntegrationAlertSettingsDto>
{
    public async Task<IntegrationAlertSettingsDto> HandleAsync(
        GetIntegrationAlertSettingsCommand command, CancellationToken ct = default)
    {
        var settings = await repository.GetIntegrationSettingsAsync(command.TenantId, command.IntegrationId, ct);
        return IntegrationAlertSettingsDto.From(command.IntegrationId, settings);
    }
}
