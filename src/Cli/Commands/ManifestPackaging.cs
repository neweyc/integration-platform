using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cronos;

namespace Cli.Commands;

// Reading and packaging of non-.NET integrations described by a serto.json manifest. The CLI ships MIT
// and is deliberately decoupled from the control-plane Shared assembly, so it parses the manifest into
// its own lightweight model — it is just another consumer of the documented JSON contract
// (docs/multi-language-runtimes.md). For .NET projects this path is skipped and the existing
// .csproj/reflection flow runs instead.
public static class ManifestPackaging
{
    public const string ManifestFileName = "serto.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Directory names never included in a packaged archive (build output, VCS, language caches).
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", "__pycache__", ".pytest_cache", ".venv", ".mypy_cache"
    };

    public static bool IsDotnetRuntime(string? runtime) =>
        string.IsNullOrEmpty(runtime) || runtime.Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    // Resolves a non-.NET manifest project from an explicit path or the current directory. Returns null
    // when the target is a .NET project (no serto.json, or one whose runtime is "dotnet"), so the caller
    // falls back to the existing .csproj path.
    public static ManifestProject? TryResolve(string? explicitPath, string currentDirectory)
    {
        var manifestPath = FindManifestPath(explicitPath, currentDirectory);
        if (manifestPath is null)
            return null;

        var manifest = Read(manifestPath);
        if (IsDotnetRuntime(manifest.Runtime))
            return null;

        var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        return new ManifestProject(manifest, directory, new DirectoryInfo(directory).Name);
    }

    public static CliManifest Read(string path)
    {
        var json = File.ReadAllText(path);
        CliManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CliManifest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{ManifestFileName} is not valid JSON: {ex.Message}");
        }

        return manifest ?? throw new InvalidOperationException($"{ManifestFileName} could not be parsed.");
    }

    // Builds the deploy/scan preview from the manifest. The agent runs the entrypoint; here we only
    // validate the declarations and show what will be provisioned.
    public static ScanResult ToScanResult(CliManifest manifest)
    {
        var integrations = new List<ScannedIntegration>();
        var errors = new List<string>();
        var secrets = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var integration in manifest.Integrations)
        {
            var triggers = integration.Triggers.Select(ToScannedTrigger).ToList();
            integrations.Add(new ScannedIntegration(
                integration.Name,
                integration.Slug,
                integration.Entrypoint,
                integration.Description,
                integration.TimeoutSeconds,
                integration.Retry?.MaxAttempts,
                integration.Retry?.BackoffSeconds,
                triggers,
                integration.RequiredCapabilities));

            errors.AddRange(Validate(integration, triggers));

            foreach (var secret in integration.RequiredSecrets.Where(s => !string.IsNullOrWhiteSpace(s)))
                secrets.Add(secret.Trim());
        }

        if (integrations.Count == 0)
            errors.Add($"{ManifestFileName} declares no integrations.");

        return new ScanResult(integrations, [], errors, secrets.ToList());
    }

    // Packages a manifest project into a zip ready for upload. There is no build step — the source plus
    // the serto.json manifest is the artifact.
    public static async Task<PackageBuildResult> CreatePackageAsync(
        ManifestProject project,
        string? packageName,
        string? packageVersion,
        string? outputDirectory,
        bool keepArchive,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var name = string.IsNullOrWhiteSpace(packageName) ? project.PackageName : packageName.Trim();
        var version = string.IsNullOrWhiteSpace(packageVersion)
            ? DeployCommand.BuildAutoVersion(now, GitInfo.Detect(project.Directory))
            : packageVersion.Trim();

        var outputDir = string.IsNullOrWhiteSpace(outputDirectory)
            ? project.Directory
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDir);

        var archivePath = Path.Combine(outputDir, DeployCommand.CreatePackageArchiveFileName(name, version));
        if (File.Exists(archivePath))
            File.Delete(archivePath);

        ZipDirectory(project.Directory, archivePath);
        var hash = await PackageCommand.ComputeSha256Async(archivePath, ct);
        var scan = ToScanResult(project.Manifest);

        if (!keepArchive && File.Exists(archivePath))
            File.Delete(archivePath);

        // ProjectName and PackageName are the same for a manifest project (there is no separate assembly
        // name). PublishDirectory is the source directory — nothing is built into a separate location.
        return new PackageBuildResult(name, name, version, archivePath, project.Directory, hash, scan);
    }

    private static string? FindManifestPath(string? explicitPath, string currentDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var full = Path.GetFullPath(explicitPath);

            if (Directory.Exists(full))
            {
                var candidate = Path.Combine(full, ManifestFileName);
                return File.Exists(candidate) ? candidate : null;
            }

            // An explicit serto.json path; anything else (e.g. a .csproj) means the .NET path.
            return string.Equals(Path.GetFileName(full), ManifestFileName, StringComparison.OrdinalIgnoreCase) && File.Exists(full)
                ? full
                : null;
        }

        var inCurrent = Path.Combine(currentDirectory, ManifestFileName);
        return File.Exists(inCurrent) ? inCurrent : null;
    }

    private static ScannedTrigger ToScannedTrigger(CliManifestTrigger trigger)
    {
        var type = (trigger.Type ?? "").ToLowerInvariant();
        return type switch
        {
            "scheduled" => new ScannedTrigger("Scheduled", "scheduled", "Scheduled", trigger.Cron),
            "webhook" => new ScannedTrigger("Webhook", "webhook", "Webhook", null),
            "message" => new ScannedTrigger("Message", "message", "Queue", null, trigger.Subject),
            "manual" => new ScannedTrigger("Manual", "manual", "Manual", null),
            _ => new ScannedTrigger(trigger.Type ?? "?", trigger.Type ?? "?", trigger.Type ?? "?", null)
        };
    }

    private static IEnumerable<string> Validate(CliManifestIntegration integration, List<ScannedTrigger> triggers)
    {
        if (string.IsNullOrWhiteSpace(integration.Name))
            yield return $"{integration.Slug}: integration name is required.";

        if (!IsSlug(integration.Slug))
            yield return $"'{integration.Slug}': slug must contain only lowercase letters, numbers, and hyphens.";

        if (string.IsNullOrWhiteSpace(integration.Entrypoint))
            yield return $"{integration.Slug}: entrypoint is required.";

        var triggerSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var trigger in triggers)
        {
            if (!triggerSlugs.Add(trigger.Slug))
                yield return $"{integration.Slug}: duplicate trigger slug '{trigger.Slug}'.";

            if (trigger.Type == "Scheduled")
            {
                if (string.IsNullOrWhiteSpace(trigger.CronExpression))
                    yield return $"{integration.Slug}: scheduled trigger requires a cron expression.";
                else if (!IsValidCron(trigger.CronExpression))
                    yield return $"{integration.Slug}: '{trigger.CronExpression}' is not a valid cron expression.";
            }
        }
    }

    private static void ZipDirectory(string sourceDirectory, string archivePath)
    {
        var root = Path.GetFullPath(sourceDirectory);
        using var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create);

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (segments.Any(segment => ExcludedDirectories.Contains(segment)))
                continue;
            if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                continue;

            // Forward-slash entry names keep archives portable across OSes.
            zip.CreateEntryFromFile(file, string.Join('/', segments));
        }
    }

    private static bool IsSlug(string slug) => Regex.IsMatch(slug, "^[a-z0-9-]+$");

    private static bool IsValidCron(string expression)
    {
        try
        {
            CronExpression.Parse(expression);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public sealed record ManifestProject(CliManifest Manifest, string Directory, string PackageName);

    // Lightweight mirror of serto.json (docs/multi-language-runtimes.md). Only the fields the CLI needs
    // for packaging and the deploy preview are modelled.
    public sealed class CliManifest
    {
        public string ManifestVersion { get; set; } = "1";
        public string Runtime { get; set; } = "dotnet";
        public List<CliManifestIntegration> Integrations { get; set; } = [];
    }

    public sealed class CliManifestIntegration
    {
        public string Name { get; set; } = "";
        public string Slug { get; set; } = "";
        public string Entrypoint { get; set; } = "";
        public string? Description { get; set; }
        public int? TimeoutSeconds { get; set; }
        public CliManifestRetry? Retry { get; set; }
        public List<string> RequiredSecrets { get; set; } = [];
        public List<string> RequiredCapabilities { get; set; } = [];
        public List<CliManifestTrigger> Triggers { get; set; } = [];
    }

    public sealed class CliManifestRetry
    {
        public int MaxAttempts { get; set; }
        public int BackoffSeconds { get; set; }
    }

    public sealed class CliManifestTrigger
    {
        public string Type { get; set; } = "";
        public string? Cron { get; set; }
        public string? Subject { get; set; }
    }
}
