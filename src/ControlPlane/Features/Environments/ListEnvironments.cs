using ControlPlane.Infrastructure;

namespace ControlPlane.Features.Environments;

public record ListEnvironmentsCommand(Guid TenantId) : ICommand<ListEnvironmentsResult>;

public record ListEnvironmentsResult(IReadOnlyList<EnvironmentDto> Environments);

public class ListEnvironmentsHandler(IEnvironmentReadRepository repository)
    : ICommandHandler<ListEnvironmentsCommand, ListEnvironmentsResult>
{
    public async Task<ListEnvironmentsResult> HandleAsync(ListEnvironmentsCommand command, CancellationToken ct = default)
    {
        var environments = await repository.ListAsync(command.TenantId, ct);
        return new ListEnvironmentsResult(environments.Select(EnvironmentDto.From).ToList());
    }
}
