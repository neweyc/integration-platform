using System.IO.Compression;
using ControlPlane.Features.IntegrationPackages;
using ControlPlane.Features.IntegrationPackages.Scanning;
using ControlPlane.Features.Integrations;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.IntegrationPackages;

public class UploadPackageHandlerTests
{
    private readonly IPackageRepository _repository = Substitute.For<IPackageRepository>();
    private readonly IAssemblyScanner _scanner = Substitute.For<IAssemblyScanner>();
    private readonly IIntegrationRepository _integrationRepository = Substitute.For<IIntegrationRepository>();
    private readonly UploadPackageHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();

    public UploadPackageHandlerTests()
    {
        _handler = new UploadPackageHandler(_repository, _scanner, _integrationRepository);
        _repository.CreateAsync(Arg.Any<AssemblyPackage>()).Returns(call => call.Arg<AssemblyPackage>());
        _scanner.ScanZip(Arg.Any<byte[]>()).Returns([]);
    }

    [Fact]
    public async Task HandleAsync_ValidZipPackage_CreatesMetadata()
    {
        var data = CreateZipWithDll();
        _repository.VersionExistsAsync(_tenantId, "MyCompany.Integrations", "1.0.0").Returns(false);

        var result = await _handler.HandleAsync(new UploadPackageCommand(
            _tenantId,
            "MyCompany.Integrations",
            "1.0.0",
            "integrations.zip",
            data));

        Assert.Equal("MyCompany.Integrations", result.Name);
        Assert.Equal("1.0.0", result.Version);
        Assert.Equal("integrations.zip", result.FileName);
        Assert.Equal(data.Length, result.SizeBytes);
        Assert.Matches("^[a-f0-9]{64}$", result.Sha256Hash);
    }

    [Fact]
    public async Task HandleAsync_DiscoveredIntegration_UpsertsPinnedIntegration()
    {
        var data = CreateZipWithDll();
        _repository.VersionExistsAsync(_tenantId, "MyCompany.Integrations", "1.0.0").Returns(false);
        _scanner.ScanZip(data).Returns([
            new DiscoveredIntegration(
                "Nightly Sync",
                "nightly-sync",
                "Acme.NightlySync",
                TriggerType.Scheduled,
                "0 0 * * *",
                "Syncs nightly data",
                TimeoutSeconds: 300,
                RetryMaxAttempts: 2,
                RetryBackoffSeconds: 60)
        ]);

        var result = await _handler.HandleAsync(new UploadPackageCommand(
            _tenantId,
            "MyCompany.Integrations",
            "1.0.0",
            "integrations.zip",
            data));

        await _integrationRepository.Received(1).UpsertBySlugAsync(Arg.Is<Integration>(i =>
            i.TenantId == _tenantId
            && i.Name == "Nightly Sync"
            && i.Slug == "nightly-sync"
            && i.Description == "Syncs nightly data"
            && i.Environment == "production"
            && i.TriggerType == TriggerType.Scheduled
            && i.CronExpression == "0 0 * * *"
            && i.ClassName == "Acme.NightlySync"
            && i.TimeoutSeconds == 300
            && i.RetryMaxAttempts == 2
            && i.RetryBackoffSeconds == 60
            && i.PackageId == result.Id
            && i.Status == IntegrationStatus.Enabled));
    }

    [Fact]
    public async Task HandleAsync_DuplicatePackageVersion_ThrowsConflictException()
    {
        var data = CreateZipWithDll();
        _repository.VersionExistsAsync(_tenantId, "MyCompany.Integrations", "1.0.0").Returns(true);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _handler.HandleAsync(new UploadPackageCommand(
                _tenantId,
                "MyCompany.Integrations",
                "1.0.0",
                "integrations.zip",
                data)));
    }

    [Fact]
    public async Task HandleAsync_ZipWithoutDll_ThrowsValidationException()
    {
        var data = CreateZip(("README.md", "docs"));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _handler.HandleAsync(new UploadPackageCommand(
                _tenantId,
                "MyCompany.Integrations",
                "1.0.0",
                "integrations.zip",
                data)));

        Assert.Equal("Package archive must contain at least one .dll file.", ex.Message);
    }

    [Fact]
    public async Task HandleAsync_NonZipFile_ThrowsValidationException()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _handler.HandleAsync(new UploadPackageCommand(
                _tenantId,
                "MyCompany.Integrations",
                "1.0.0",
                "integrations.txt",
                [1, 2, 3])));

        Assert.Equal("Package file must be a .zip archive.", ex.Message);
    }

    private static byte[] CreateZipWithDll() =>
        CreateZip(("MyCompany.Integrations.dll", "binary"));

    private static byte[] CreateZip(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                var zipEntry = archive.CreateEntry(entry.Name);
                using var writer = new StreamWriter(zipEntry.Open());
                writer.Write(entry.Content);
            }
        }

        return stream.ToArray();
    }
}
