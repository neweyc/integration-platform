using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Tenants;

public record CreateTenantCommand(string Name, string Slug) : ICommand<CreateTenantResult>;

public record CreateTenantResult(Guid Id, string Name, string Slug);

public interface ITenantRepository
{
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);
    Task<Tenant> CreateAsync(Tenant tenant, CancellationToken ct = default);
}

public class CreateTenantHandler(ITenantRepository repository)
    : ICommandHandler<CreateTenantCommand, CreateTenantResult>
{
    public async Task<CreateTenantResult> HandleAsync(CreateTenantCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ValidationException("Name is required.");

        if (string.IsNullOrWhiteSpace(command.Slug))
            throw new ValidationException("Slug is required.");

        if (!System.Text.RegularExpressions.Regex.IsMatch(command.Slug, @"^[a-z0-9-]+$"))
            throw new ValidationException("Slug may only contain lowercase letters, numbers, and hyphens.");

        if (await repository.SlugExistsAsync(command.Slug, ct))
            throw new ConflictException($"Slug '{command.Slug}' is already taken.");

        var tenant = new Tenant
        {
            Name = command.Name,
            Slug = command.Slug
        };

        var created = await repository.CreateAsync(tenant, ct);

        return new CreateTenantResult(created.Id, created.Name, created.Slug);
    }
}
