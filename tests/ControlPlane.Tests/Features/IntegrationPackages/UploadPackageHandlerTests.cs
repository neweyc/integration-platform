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
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly UploadPackageHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();

    public UploadPackageHandlerTests()
    {
        _handler = new UploadPackageHandler(_repository, _scanner, _integrationRepository, _encryption);
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
                "Syncs nightly data",
                TimeoutSeconds: 300,
                RetryMaxAttempts: 2,
                RetryBackoffSeconds: 60,
                [
                    new DiscoveredIntegrationTrigger("Scheduled", "scheduled", TriggerType.Scheduled, "0 0 * * *")
                ])
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
            && i.ClassName == "Acme.NightlySync"
            && i.TimeoutSeconds == 300
            && i.RetryMaxAttempts == 2
            && i.RetryBackoffSeconds == 60
            && i.PackageId == result.Id
            && i.Status == IntegrationStatus.Enabled),
            Arg.Is<IReadOnlyList<IntegrationTrigger>>(triggers =>
                triggers.Count == 1
                && triggers[0].Name == "Scheduled"
                && triggers[0].Slug == "scheduled"
                && triggers[0].Type == TriggerType.Scheduled
                && triggers[0].CronExpression == "0 0 * * *"));
    }

    [Fact]
    public async Task HandleAsync_DiscoveredIntegrationWithMultipleTriggers_UpsertsTriggerRecords()
    {
        var data = CreateZipWithDll();
        _repository.VersionExistsAsync(_tenantId, "MyCompany.Integrations", "1.0.0").Returns(false);
        _scanner.ScanZip(data).Returns([
            new DiscoveredIntegration(
                "Order Sync",
                "order-sync",
                "Acme.OrderSync",
                "Syncs orders",
                TimeoutSeconds: 120,
                RetryMaxAttempts: 1,
                RetryBackoffSeconds: 30,
                [
                    new DiscoveredIntegrationTrigger("Scheduled", "scheduled", TriggerType.Scheduled, "*/5 * * * *"),
                    new DiscoveredIntegrationTrigger("Webhook", "webhook", TriggerType.Webhook, CronExpression: null)
                ])
        ]);

        await _handler.HandleAsync(new UploadPackageCommand(
            _tenantId,
            "MyCompany.Integrations",
            "1.0.0",
            "integrations.zip",
            data));

        await _integrationRepository.Received(1).UpsertBySlugAsync(
            Arg.Any<Integration>(),
            Arg.Is<IReadOnlyList<IntegrationTrigger>>(triggers =>
                triggers.Count == 2
                && triggers.Any(t => t.Name == "Scheduled"
                                 && t.Slug == "scheduled"
                                 && t.Type == TriggerType.Scheduled
                                 && t.CronExpression == "*/5 * * * *")
                && triggers.Any(t => t.Name == "Webhook"
                                 && t.Slug == "webhook"
                                 && t.Type == TriggerType.Webhook
                                 && t.CronExpression == null
                                 && t.EncryptedWebhookSecret != null)));
    }

    [Fact]
    public async Task HandleAsync_DiscoveredIntegrationWithInvalidCron_DoesNotCreatePackage()
    {
        var data = CreateZipWithDll();
        _repository.VersionExistsAsync(_tenantId, "MyCompany.Integrations", "1.0.0").Returns(false);
        _scanner.ScanZip(data).Returns([
            new DiscoveredIntegration(
                "Nightly Sync",
                "nightly-sync",
                "Acme.NightlySync",
                Description: null,
                TimeoutSeconds: null,
                RetryMaxAttempts: null,
                RetryBackoffSeconds: null,
                [
                    new DiscoveredIntegrationTrigger("Scheduled", "scheduled", TriggerType.Scheduled, "not-a-cron")
                ])
        ]);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _handler.HandleAsync(new UploadPackageCommand(
                _tenantId,
                "MyCompany.Integrations",
                "1.0.0",
                "integrations.zip",
                data)));

        await _repository.DidNotReceive().CreateAsync(Arg.Any<AssemblyPackage>(), Arg.Any<CancellationToken>());
        await _integrationRepository.DidNotReceive().UpsertBySlugAsync(
            Arg.Any<Integration>(),
            Arg.Any<IReadOnlyList<IntegrationTrigger>>(),
            Arg.Any<CancellationToken>());
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
