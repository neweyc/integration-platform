namespace Shared.Domain;

/// <summary>
/// The single place environment names are canonicalized. Every write path that accepts an
/// environment string runs it through <see cref="Normalize"/> so that comparisons elsewhere
/// (agent token scope vs. secret scope, integration vs. workflow) are always against the same
/// canonical form — trimmed and lowercased — instead of relying on callers to get casing right.
/// </summary>
public static class EnvironmentKey
{
    public const int MaxLength = 50;

    // Lowercase letters, numbers, and hyphens. Matches the slug convention used elsewhere
    // (tenant slugs, integration slugs) so environment names stay URL- and config-safe.
    private static readonly System.Text.RegularExpressions.Regex Pattern =
        new("^[a-z0-9-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Trims and lowercases an environment name. Returns an empty string for null/whitespace input
    /// so callers can apply their own "required" validation consistently.
    /// </summary>
    public static string Normalize(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim().ToLowerInvariant();

    public static bool IsValid(string normalized) =>
        normalized.Length is > 0 and <= MaxLength && Pattern.IsMatch(normalized);
}
