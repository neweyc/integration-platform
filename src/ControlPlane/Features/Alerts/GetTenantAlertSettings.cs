using ControlPlane.Features.Alerts.Email;
using ControlPlane.Infrastructure;

namespace ControlPlane.Features.Alerts;

public record GetTenantAlertSettingsCommand(Guid TenantId) : ICommand<TenantAlertSettingsDto>;

public class GetTenantAlertSettingsHandler(IAlertSettingsReadRepository repository, ZeptoOptions zepto)
    : ICommandHandler<GetTenantAlertSettingsCommand, TenantAlertSettingsDto>
{
    public async Task<TenantAlertSettingsDto> HandleAsync(
        GetTenantAlertSettingsCommand command, CancellationToken ct = default)
    {
        var settings = await repository.GetTenantSettingsAsync(command.TenantId, ct);
        return TenantAlertSettingsDto.From(settings, zepto);
    }
}
