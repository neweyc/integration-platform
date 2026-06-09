using Environment = Shared.Domain.Environment;

namespace ControlPlane.Features.Environments;

public record EnvironmentDto(
    string Name,
    string DisplayName,
    string? Description,
    int SortOrder,
    bool IsDefault)
{
    public static EnvironmentDto From(Environment environment) =>
        new(environment.Name, environment.DisplayName, environment.Description, environment.SortOrder, environment.IsDefault);
}

// DisplayName/Description are optional; the name is the canonical key and is required.
public record CreateEnvironmentRequest(
    string Name,
    string? DisplayName,
    string? Description,
    int? SortOrder,
    bool IsDefault);

// Name is immutable (it is the key other records reference), so only presentational fields and the
// default flag can be updated.
public record UpdateEnvironmentRequest(
    string? DisplayName,
    string? Description,
    int? SortOrder,
    bool IsDefault);
