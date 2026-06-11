using System.Text.Json;

namespace Shared.Manifest;

// The declarative description of a package's contents — the `serto.json` at the root of an integration
// archive. For non-.NET runtimes this is the source of truth that replaces server-side assembly
// reflection; for .NET the CLI can still generate it by reflecting over the build output. See
// docs/multi-language-runtimes.md for the full contract.
//
// These are deserialization POCOs: init-only properties with safe defaults so an absent field (e.g. no
// triggers) reads as empty rather than null. Read/write them through ManifestJson.Options.
public sealed class PackageManifest
{
    public string ManifestVersion { get; init; } = "1";

    // Selects the agent runner and is stamped onto every dispatched work item. See Runtimes.
    public string Runtime { get; init; } = Runtimes.Dotnet;

    public IReadOnlyList<ManifestIntegration> Integrations { get; init; } = [];
}

public sealed class ManifestIntegration
{
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;

    // Runtime-specific locator interpreted only by the agent's runner: a .NET class name, a
    // "module:function" for Python, a "file#export" for Node, a binary path for Go, etc. Generalizes
    // the .NET-only Integration.ClassName.
    public string Entrypoint { get; init; } = string.Empty;

    public string? Description { get; init; }
    public int? TimeoutSeconds { get; init; }
    public ManifestRetry? Retry { get; init; }

    // Secret names the platform must provision for this integration — names only, never values.
    public IReadOnlyList<string> RequiredSecrets { get; init; } = [];

    // Agent capability tags this integration requires for routing.
    public IReadOnlyList<string> RequiredCapabilities { get; init; } = [];

    public IReadOnlyList<ManifestTrigger> Triggers { get; init; } = [];
}

public sealed class ManifestRetry
{
    public int MaxAttempts { get; init; }
    public int BackoffSeconds { get; init; }
}

public sealed class ManifestTrigger
{
    // One of TriggerTypes. Kept as a string in the manifest (a language-neutral file format) and mapped
    // to the domain TriggerType enum on ingest.
    public string Type { get; init; } = string.Empty;

    public string? Cron { get; init; }      // scheduled
    public string? Subject { get; init; }   // message
}

// Well-known runtime identifiers for PackageManifest.Runtime / the dispatched work item.
public static class Runtimes
{
    public const string Dotnet = "dotnet";
    public const string Python = "python";
    public const string Node = "node";
    public const string Go = "go";
    public const string Container = "container";

    // Absent or "dotnet" means the in-process .NET fast path. Treating absent as dotnet keeps every
    // existing package — which carries no runtime — running unchanged.
    public static bool IsDotnet(string? runtime) =>
        string.IsNullOrEmpty(runtime) || runtime.Equals(Dotnet, StringComparison.OrdinalIgnoreCase);
}

// Well-known manifest trigger type identifiers (lower-case in the file format).
public static class TriggerTypes
{
    public const string Scheduled = "scheduled";
    public const string Webhook = "webhook";
    public const string Message = "message";
    public const string Manual = "manual";
}

// Canonical JSON options for reading and writing serto.json. camelCase, case-insensitive on read, and
// omit null properties on write so generated manifests stay clean.
public static class ManifestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
}
