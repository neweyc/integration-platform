using System.IO.Compression;
using Cli.Commands;
using Xunit;

namespace Cli.Tests;

public class ManifestPackagingTests
{
    [Fact]
    public void ToScanResult_MapsIntegrationTriggersAndSecrets()
    {
        var manifest = new ManifestPackaging.CliManifest
        {
            Runtime = "python",
            Integrations =
            [
                new ManifestPackaging.CliManifestIntegration
                {
                    Name = "Sync Orders",
                    Slug = "sync-orders",
                    Entrypoint = "main.py:run",
                    RequiredSecrets = ["API_KEY"],
                    Triggers = [new ManifestPackaging.CliManifestTrigger { Type = "scheduled", Cron = "0 * * * *" }]
                }
            ]
        };

        var result = ManifestPackaging.ToScanResult(manifest);

        Assert.True(result.IsValid);
        var integration = Assert.Single(result.Integrations);
        Assert.Equal("main.py:run", integration.ClassName);
        Assert.Equal("Scheduled", Assert.Single(integration.Triggers).Type);
        Assert.Equal(["API_KEY"], result.RequiredSecrets);
    }

    [Fact]
    public void ToScanResult_ReportsInvalidSlugEntrypointAndCron()
    {
        var manifest = new ManifestPackaging.CliManifest
        {
            Runtime = "python",
            Integrations =
            [
                new ManifestPackaging.CliManifestIntegration
                {
                    Name = "Bad",
                    Slug = "Bad Slug",
                    Entrypoint = "",
                    Triggers = [new ManifestPackaging.CliManifestTrigger { Type = "scheduled", Cron = "not-a-cron" }]
                }
            ]
        };

        var result = ManifestPackaging.ToScanResult(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("slug"));
        Assert.Contains(result.Errors, e => e.Contains("entrypoint"));
        Assert.Contains(result.Errors, e => e.Contains("cron"));
    }

    [Fact]
    public async Task CreatePackageAsync_ZipsSourceWithManifest_AndExcludesBuildOutput()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), "serto-cli-pkg-" + Guid.NewGuid().ToString("N"));
        var outputDir = Path.Combine(Path.GetTempPath(), "serto-cli-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDir);

        await File.WriteAllTextAsync(Path.Combine(projectDir, "serto.json"),
            """{ "manifestVersion": "1", "runtime": "python", "integrations": [ { "name": "X", "slug": "x", "entrypoint": "main.py:handler" } ] }""");
        await File.WriteAllTextAsync(Path.Combine(projectDir, "main.py"), "def handler(ctx): pass\n");
        Directory.CreateDirectory(Path.Combine(projectDir, "bin"));
        await File.WriteAllTextAsync(Path.Combine(projectDir, "bin", "junk.dll"), "noise");

        try
        {
            var manifest = ManifestPackaging.Read(Path.Combine(projectDir, "serto.json"));
            var project = new ManifestPackaging.ManifestProject(manifest, projectDir, "x");

            var result = await ManifestPackaging.CreatePackageAsync(
                project, "x", "1.0.0", outputDir, keepArchive: true, DateTimeOffset.UtcNow, CancellationToken.None);

            using var zip = ZipFile.OpenRead(result.ArchivePath);
            var entries = zip.Entries.Select(e => e.FullName).ToList();

            Assert.Contains("serto.json", entries);
            Assert.Contains("main.py", entries);
            Assert.DoesNotContain(entries, e => e.Contains("bin/"));
            Assert.Equal("1.0.0", result.PackageVersion);
        }
        finally
        {
            try { Directory.Delete(projectDir, recursive: true); } catch { /* best effort */ }
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
