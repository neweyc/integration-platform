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

    // The routing rule: an agent can run an integration iff it offers every tag the integration
    // requires (subset/AND). No required tags ⇒ runnable on any agent.
    public static bool IsSatisfiedBy(IReadOnlyCollection<string> required, IReadOnlyCollection<string> offered)
    {
        if (required.Count == 0)
            return true;

        var set = new HashSet<string>(offered, StringComparer.OrdinalIgnoreCase);
        return required.All(set.Contains);
    }
}
