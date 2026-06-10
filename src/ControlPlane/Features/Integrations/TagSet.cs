namespace ControlPlane.Features.Integrations;

// Helpers for agent-capability tag sets. Tags are an unordered, case-insensitive set, so equality
// ignores order and Normalize trims, drops blanks, and de-duplicates.
public static class TagSet
{
    public static string[] Normalize(IEnumerable<string>? tags) =>
        (tags ?? [])
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    // Order-insensitive set equality. Used to decide whether an integration's active required tags
    // have diverged from the code-declared defaults (i.e. an operator override is in effect).
    public static bool Equal(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
    {
        if (a.Count != b.Count)
            return false;

        var set = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
        return b.All(set.Contains);
    }
}
