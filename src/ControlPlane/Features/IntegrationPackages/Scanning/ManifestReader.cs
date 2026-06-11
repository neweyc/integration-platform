using System.IO.Compression;
using System.Text.Json;
using ControlPlane.Infrastructure;
using Shared.Domain;
using Shared.Manifest;

namespace ControlPlane.Features.IntegrationPackages.Scanning;

// Reads a package's serto.json manifest. For non-.NET runtimes the manifest is the source of truth for
// what a package contains, replacing assembly reflection (which can't inspect a Python/Node package).
// See docs/multi-language-runtimes.md.
public interface IManifestReader
{
    // The parsed manifest if the archive contains a root-level serto.json, otherwise null (a .NET package
    // to be discovered by reflection instead).
    PackageManifest? TryRead(byte[] zipData);
}

public class ManifestReader : IManifestReader
{
    private const string ManifestEntryName = "serto.json";

    public PackageManifest? TryRead(byte[] zipData)
    {
        using var stream = new MemoryStream(zipData);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var entry = archive.Entries.FirstOrDefault(e =>
            string.Equals(e.Name, ManifestEntryName, StringComparison.OrdinalIgnoreCase) && IsRootLevel(e.FullName));
        if (entry is null)
            return null;

        using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);
        var json = reader.ReadToEnd();

        PackageManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PackageManifest>(json, ManifestJson.Options);
        }
        catch (JsonException ex)
        {
            throw new ValidationException($"serto.json is present but is not valid JSON: {ex.Message}");
        }

        if (manifest is null || manifest.Integrations.Count == 0)
            throw new ValidationException("serto.json must declare at least one integration.");

        return manifest;
    }

    // Maps a manifest into the same DiscoveredIntegration shape the reflection scanner produces, so the
    // upload handler provisions both runtimes through one path.
    public static List<DiscoveredIntegration> ToDiscovered(PackageManifest manifest) =>
        manifest.Integrations.Select(integration => new DiscoveredIntegration(
            integration.Name,
            integration.Slug,
            integration.Entrypoint,
            integration.Description,
            integration.TimeoutSeconds,
            integration.Retry?.MaxAttempts,
            integration.Retry?.BackoffSeconds,
            integration.Triggers.Select(ToTrigger).ToList(),
            integration.RequiredCapabilities)).ToList();

    private static DiscoveredIntegrationTrigger ToTrigger(ManifestTrigger trigger)
    {
        var type = (trigger.Type ?? "").ToLowerInvariant();
        return type switch
        {
            TriggerTypes.Scheduled => new DiscoveredIntegrationTrigger("Scheduled", "scheduled", TriggerType.Scheduled, trigger.Cron),
            TriggerTypes.Webhook => new DiscoveredIntegrationTrigger("Webhook", "webhook", TriggerType.Webhook, null),
            TriggerTypes.Message => new DiscoveredIntegrationTrigger("Message", "message", TriggerType.Queue, null, trigger.Subject),
            TriggerTypes.Manual => new DiscoveredIntegrationTrigger("Manual", "manual", TriggerType.Manual, null),
            _ => throw new ValidationException($"Unknown trigger type '{trigger.Type}' in serto.json.")
        };
    }

    private static bool IsRootLevel(string fullName) =>
        !fullName.Replace('\\', '/').TrimEnd('/').Contains('/');
}
