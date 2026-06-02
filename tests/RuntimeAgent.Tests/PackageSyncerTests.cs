using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RuntimeAgent.Agent;
using RuntimeAgent.Execution;

namespace RuntimeAgent.Tests;

public class PackageSyncerTests : IDisposable
{
    private readonly IControlPlaneClient _controlPlane = Substitute.For<IControlPlaneClient>();
    private readonly IntegrationLoader _loader;
    private readonly string _packagesDir;
    private readonly AgentOptions _options;
    private readonly PackageSyncer _syncer;

    public PackageSyncerTests()
    {
        _packagesDir = Path.Combine(Path.GetTempPath(), "pkg-sync-tests-" + Guid.NewGuid());
        _loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);
        _options = new AgentOptions
        {
            PackagesPath = _packagesDir,
            IntegrationsPath = "."
        };
        _syncer = new PackageSyncer(_controlPlane, _loader, _options, NullLogger<PackageSyncer>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_packagesDir))
            Directory.Delete(_packagesDir, recursive: true);
    }

    [Fact]
    public async Task SyncAsync_NewPackage_DownloadsExtractsAndWritesHash()
    {
        var (zipBytes, hash) = MakeZip("test.dll");
        var pkg = new AgentPackageInfo(Guid.NewGuid(), "My.Package", "1.0.0", hash);

        _controlPlane.ListPackagesAsync(Arg.Any<CancellationToken>()).Returns([pkg]);
        _controlPlane.DownloadPackageAsync(pkg.Id, Arg.Any<CancellationToken>()).Returns(zipBytes);

        await _syncer.SyncAsync(CancellationToken.None);

        var packageDir = Path.Combine(_packagesDir, pkg.Id.ToString());
        Assert.True(Directory.Exists(packageDir));
        Assert.True(File.Exists(Path.Combine(packageDir, ".hash")));
        Assert.Equal(hash, (await File.ReadAllTextAsync(Path.Combine(packageDir, ".hash"))).Trim());
    }

    [Fact]
    public async Task SyncAsync_AlreadySyncedPackage_SkipsDownload()
    {
        var (zipBytes, hash) = MakeZip("test.dll");
        var pkg = new AgentPackageInfo(Guid.NewGuid(), "My.Package", "1.0.0", hash);

        // Pre-create the package directory with the correct hash file
        var packageDir = Path.Combine(_packagesDir, pkg.Id.ToString());
        Directory.CreateDirectory(packageDir);
        await File.WriteAllTextAsync(Path.Combine(packageDir, ".hash"), hash);

        _controlPlane.ListPackagesAsync(Arg.Any<CancellationToken>()).Returns([pkg]);

        await _syncer.SyncAsync(CancellationToken.None);

        await _controlPlane.DidNotReceive().DownloadPackageAsync(pkg.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAsync_HashMismatch_SkipsExtraction()
    {
        var (zipBytes, _) = MakeZip("test.dll");
        var pkg = new AgentPackageInfo(Guid.NewGuid(), "My.Package", "1.0.0", "deadbeef");

        _controlPlane.ListPackagesAsync(Arg.Any<CancellationToken>()).Returns([pkg]);
        _controlPlane.DownloadPackageAsync(pkg.Id, Arg.Any<CancellationToken>()).Returns(zipBytes);

        await _syncer.SyncAsync(CancellationToken.None);

        var packageDir = Path.Combine(_packagesDir, pkg.Id.ToString());
        Assert.False(Directory.Exists(packageDir));
    }

    [Fact]
    public async Task SyncAsync_ControlPlaneError_DoesNotThrow()
    {
        _controlPlane.ListPackagesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var ex = await Record.ExceptionAsync(() => _syncer.SyncAsync(CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task SyncAsync_DownloadError_SkipsPackageAndContinues()
    {
        var (zipBytes, hash) = MakeZip("test.dll");
        var bad = new AgentPackageInfo(Guid.NewGuid(), "Bad.Package", "1.0.0", "anyhash");
        var good = new AgentPackageInfo(Guid.NewGuid(), "Good.Package", "1.0.0", hash);

        _controlPlane.ListPackagesAsync(Arg.Any<CancellationToken>()).Returns([bad, good]);
        _controlPlane.DownloadPackageAsync(bad.Id, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("download failed"));
        _controlPlane.DownloadPackageAsync(good.Id, Arg.Any<CancellationToken>()).Returns(zipBytes);

        await _syncer.SyncAsync(CancellationToken.None);

        var goodDir = Path.Combine(_packagesDir, good.Id.ToString());
        Assert.True(Directory.Exists(goodDir));
    }

    [Fact]
    public async Task SyncAsync_NoPackages_DoesNothing()
    {
        _controlPlane.ListPackagesAsync(Arg.Any<CancellationToken>()).Returns([]);

        await _syncer.SyncAsync(CancellationToken.None);

        Assert.False(Directory.Exists(_packagesDir));
        await _controlPlane.DidNotReceive().DownloadPackageAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private static (byte[] Zip, string Hash) MakeZip(string entryName)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var stream = entry.Open();
            stream.Write([0x4D, 0x5A]); // minimal PE header marker
        }

        var bytes = ms.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return (bytes, hash);
    }
}
