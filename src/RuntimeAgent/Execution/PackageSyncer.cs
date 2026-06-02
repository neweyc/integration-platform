using System.IO.Compression;
using System.Security.Cryptography;
using RuntimeAgent.Agent;

namespace RuntimeAgent.Execution;

// Downloads packages from the control plane, verifies their SHA-256 hash,
// extracts them to disk, and registers their integrations with the loader.
public class PackageSyncer(
    IControlPlaneClient controlPlane,
    IntegrationLoader loader,
    AgentOptions options,
    ILogger<PackageSyncer> logger)
{
    public async Task SyncAsync(CancellationToken ct)
    {
        List<AgentPackageInfo> packages;
        try
        {
            packages = await controlPlane.ListPackagesAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Package sync failed — could not reach control plane");
            return;
        }

        if (packages is not { Count: > 0 })
            return;

        Directory.CreateDirectory(options.PackagesPath);

        foreach (var pkg in packages)
        {
            if (ct.IsCancellationRequested) break;
            await SyncPackageAsync(pkg, ct);
        }
    }

    private async Task SyncPackageAsync(AgentPackageInfo pkg, CancellationToken ct)
    {
        var packageDir = Path.Combine(options.PackagesPath, pkg.Id.ToString());
        var hashFile = Path.Combine(packageDir, ".hash");

        // Already downloaded and verified — just ensure it's loaded
        if (File.Exists(hashFile))
        {
            var storedHash = (await File.ReadAllTextAsync(hashFile, ct)).Trim();
            if (storedHash == pkg.Sha256Hash)
            {
                loader.LoadFromDirectory(packageDir);
                return;
            }
        }

        logger.LogInformation("Downloading package {Name} v{Version} ({Id})", pkg.Name, pkg.Version, pkg.Id);

        byte[] data;
        try
        {
            data = await controlPlane.DownloadPackageAsync(pkg.Id, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to download package {Name} v{Version}", pkg.Name, pkg.Version);
            return;
        }

        var actualHash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        if (actualHash != pkg.Sha256Hash)
        {
            logger.LogError(
                "Package {Name} v{Version} hash mismatch (expected {Expected}, got {Actual}) — skipping",
                pkg.Name, pkg.Version, pkg.Sha256Hash, actualHash);
            return;
        }

        // Extract to a temp directory first to avoid leaving a partially written package on disk
        var tempDir = packageDir + ".tmp";
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        try
        {
            using var ms = new MemoryStream(data);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            archive.ExtractToDirectory(tempDir, overwriteFiles: true);

            await File.WriteAllTextAsync(Path.Combine(tempDir, ".hash"), pkg.Sha256Hash, ct);

            if (Directory.Exists(packageDir)) Directory.Delete(packageDir, true);
            Directory.Move(tempDir, packageDir);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to extract package {Name} v{Version}", pkg.Name, pkg.Version);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            return;
        }

        logger.LogInformation("Package {Name} v{Version} installed at {Dir}", pkg.Name, pkg.Version, packageDir);
        loader.LoadFromDirectory(packageDir);
    }
}
