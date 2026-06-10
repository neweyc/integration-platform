using System.IO.Compression;
using ControlPlane.Features.Environments;
using ControlPlane.Features.IntegrationPackages;
using ControlPlane.Features.IntegrationPackages.Scanning;
using ControlPlane.Features.Integrations;
using ControlPlane.Features.Secrets;
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
    private readonly ISecretReadRepository _secretRepository = Substitute.For<ISecretReadRepository>();
    private readonly IEnvironmentReadRepository _environmentRepository = Substitute.For<IEnvironmentReadRepository>();
    private readonly UploadPackageHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();

    public UploadPackageHandlerTests()
    {
        _handler = new UploadPackageHandler(
            _repository, _scanner, _integrationRepository, _encryption, _secretRepository, _environmentRepository);
        // Auto-provisioning targets the tenant's default environment.
        _environmentRepository.GetDefaultNameAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns("production");
        _repository.CreateAsync(Arg.Any<AssemblyPackage>()).Returns(call => call.Arg<AssemblyPackage>());
        _secretRepository.ListAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<Secret>());
        _integrationRepository.GetTenantSlugAsync(_tenantId).Returns("acme");
        _integrationRepository.UpsertBySlugAsync(
                Arg.Any<Integration>(),
                Arg.Any<IReadOnlyList<IntegrationTrigger>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var integration = call.Arg<Integration>();
                var triggers = call.Arg<IReadOnlyList<IntegrationTrigger>>();
                foreach (var trigger in triggers)
                    integration.Triggers.Add(trigger);

                return new IntegrationUpsertResult(
                    integration,
                    Created: true,
                    triggers.Select(t => new IntegrationTriggerUpsertResult(
                        t,
                        Created: true,
                        WebhookSecretPreserved: false)).ToList());
            });
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

        Assert.Equal("MyCompany.Integrations", result.Package.Name);
        Assert.Equal("1.0.0", result.Package.Version);
        Assert.Equal("integrations.zip", result.Package.FileName);
        Assert.Equal(data.Length, result.Package.SizeBytes);
        Assert.Matches("^[a-f0-9]{64}$", result.Package.Sha256Hash);
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
            && i.PackageId == result.Package.Id
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
    public async Task HandleAsync_ReturnsProvisioningReport()
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
                    new DiscoveredIntegrationTrigger("Every Five", "every-five", TriggerType.Scheduled, "*/5 * * * *"),
                    new DiscoveredIntegrationTrigger("Hook", "hook", TriggerType.Webhook, CronExpression: null)
                ])
        ]);

        var result = await _handler.HandleAsync(new UploadPackageCommand(
            _tenantId,
            "MyCompany.Integrations",
            "1.0.0",
            "integrations.zip",
            data));

        var provisioned = Assert.Single(result.Provisioning);
        Assert.Equal("Created", provisioned.Action);
        Assert.Equal("order-sync", provisioned.Slug);
        Assert.Equal(result.Package.Id, provisioned.PackageId);
        Assert.Contains(provisioned.Triggers, t =>
            t.Slug == "every-five"
            && t.Action == "Created"
            && t.CronExpression == "*/5 * * * *"
            && t.NextRunAt is not null);
        Assert.Contains(provisioned.Triggers, t =>
            t.Slug == "hook"
            && t.Action == "Created"
            && t.WebhookUrl == "/webhooks/acme/order-sync/hook"
            && !t.WebhookSecretPreserved);
    }

    [Fact]
    public async Task HandleAsync_ExistingWebhookTrigger_ReportsPreservedSecret()
    {
        var data = CreateZipWithDll();
        _repository.VersionExistsAsync(_tenantId, "MyCompany.Integrations", "1.0.0").Returns(false);
        _scanner.ScanZip(data).Returns([
            new DiscoveredIntegration(
                "Order Sync",
                "order-sync",
                "Acme.OrderSync",
                Description: null,
                TimeoutSeconds: null,
                RetryMaxAttempts: null,
                RetryBackoffSeconds: null,
                [
                    new DiscoveredIntegrationTrigger("Hook", "hook", TriggerType.Webhook, CronExpression: null)
                ])
        ]);
        _integrationRepository.UpsertBySlugAsync(
                Arg.Any<Integration>(),
                Arg.Any<IReadOnlyList<IntegrationTrigger>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var integration = call.Arg<Integration>();
                var trigger = call.Arg<IReadOnlyList<IntegrationTrigger>>().Single();
                integration.Triggers.Add(trigger);

                return new IntegrationUpsertResult(
                    integration,
                    Created: false,
                    [new IntegrationTriggerUpsertResult(trigger, Created: false, WebhookSecretPreserved: true)]);
            });

        var result = await _handler.HandleAsync(new UploadPackageCommand(
            _tenantId,
            "MyCompany.Integrations",
            "1.0.0",
            "integrations.zip",
            data));

        var provisioned = Assert.Single(result.Provisioning);
        Assert.Equal("Updated", provisioned.Action);
        var trigger = Assert.Single(provisioned.Triggers);
        Assert.Equal("Updated", trigger.Action);
        Assert.True(trigger.WebhookSecretPreserved);
    }

    [Fact]
    public async Task HandleAsync_ReportsPreservedTriggerOverridesAsDrift()
    {
        var data = CreateZipWithDll();
        _repository.VersionExistsAsync(_tenantId, "MyCompany.Integrations", "1.0.0").Returns(false);
        _scanner.ScanZip(data).Returns([
            new DiscoveredIntegration(
                "Order Sync", "order-sync", "Acme.OrderSync",
                Description: null, TimeoutSeconds: null, RetryMaxAttempts: null, RetryBackoffSeconds: null,
                [new DiscoveredIntegrationTrigger("Schedule", "schedule", TriggerType.Scheduled, "30 1 * * *")])
        ]);
        _integrationRepository.UpsertBySlugAsync(
                Arg.Any<Integration>(),
                Arg.Any<IReadOnlyList<IntegrationTrigger>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var integration = call.Arg<Integration>();
                var trigger = call.Arg<IReadOnlyList<IntegrationTrigger>>().Single();
                // Simulate operator overrides preserved across the redeploy.
                trigger.CronExpression = "*/5 * * * *";
                trigger.DeclaredCronExpression = "30 1 * * *";
                trigger.Enabled = false;
                trigger.DeclaredEnabled = true;
                integration.Triggers.Add(trigger);

                return new IntegrationUpsertResult(
                    integration,
                    Created: false,
                    [new IntegrationTriggerUpsertResult(
                        trigger, Created: false, WebhookSecretPreserved: false,
                        CronOverridden: true, EnabledOverridden: true)]);
            });

        var result = await _handler.HandleAsync(new UploadPackageCommand(
            _tenantId, "MyCompany.Integrations", "1.0.0", "integrations.zip", data));

        var trigger = Assert.Single(Assert.Single(result.Provisioning).Triggers);
        Assert.True(trigger.CronOverridden);
        Assert.True(trigger.EnabledOverridden);
        Assert.Equal("*/5 * * * *", trigger.CronExpression);
        Assert.Equal("30 1 * * *", trigger.DeclaredCronExpression);
    }

    [Fact]
    public async Task HandleAsync_ProvisionsRequiredTagsFromCode()
    {
        var data = CreateZipWithDll();
        _repository.VersionExistsAsync(_tenantId, "MyCompany.Integrations", "1.0.0").Returns(false);
        _scanner.ScanZip(data).Returns([
            new DiscoveredIntegration(
                "Reactor Pulse", "reactor-pulse", "Acme.ReactorPulse",
                Description: null, TimeoutSeconds: null, RetryMaxAttempts: null, RetryBackoffSeconds: null,
                Triggers: [],
                RequiredTags: ["hardware-signal", "gpu"])
        ]);

        Integration? captured = null;
        _integrationRepository.UpsertBySlugAsync(
                Arg.Any<Integration>(),
                Arg.Any<IReadOnlyList<IntegrationTrigger>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<Integration>();
                return new IntegrationUpsertResult(captured, Created: true, []);
            });

        var result = await _handler.HandleAsync(new UploadPackageCommand(
            _tenantId, "MyCompany.Integrations", "1.0.0", "integrations.zip", data));

        // Code-driven build hands the scanner's tags to the upsert as both active and declared.
        Assert.NotNull(captured);
        Assert.Equal(["hardware-signal", "gpu"], captured!.RequiredTags);
        Assert.Equal(["hardware-signal", "gpu"], captured.DeclaredRequiredTags);

        var provisioned = Assert.Single(result.Provisioning);
        Assert.Equal(["hardware-signal", "gpu"], provisioned.RequiredTags);
        Assert.False(provisioned.RequiredTagsOverridden);
    }

    [Fact]
    public async Task HandleAsync_ReportsPreservedRequiredTagsOverrideAsDrift()
    {
        var data = CreateZipWithDll();
        _repository.VersionExistsAsync(_tenantId, "MyCompany.Integrations", "1.0.0").Returns(false);
        _scanner.ScanZip(data).Returns([
            new DiscoveredIntegration(
                "Reactor Pulse", "reactor-pulse", "Acme.ReactorPulse",
                Description: null, TimeoutSeconds: null, RetryMaxAttempts: null, RetryBackoffSeconds: null,
                Triggers: [],
                RequiredTags: ["hardware-signal", "gpu"])
        ]);
        _integrationRepository.UpsertBySlugAsync(
                Arg.Any<Integration>(),
                Arg.Any<IReadOnlyList<IntegrationTrigger>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var integration = call.Arg<Integration>();
                // Simulate an operator override (narrowed to one tag) preserved across the redeploy.
                integration.RequiredTags = ["hardware-signal"];
                integration.DeclaredRequiredTags = ["hardware-signal", "gpu"];
                return new IntegrationUpsertResult(integration, Created: false, [], RequiredTagsOverridden: true);
            });

        var result = await _handler.HandleAsync(new UploadPackageCommand(
            _tenantId, "MyCompany.Integrations", "1.0.0", "integrations.zip", data));

        var provisioned = Assert.Single(result.Provisioning);
        Assert.True(provisioned.RequiredTagsOverridden);
        Assert.Equal(["hardware-signal"], provisioned.RequiredTags);
        Assert.Equal(["hardware-signal", "gpu"], provisioned.DeclaredRequiredTags);
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
    public async Task HandleAsync_NoRequiredSecrets_ReturnsEmptySecretCheck()
    {
        var data = CreateZipWithDll();
        _repository.VersionExistsAsync(_tenantId, "MyCompany.Integrations", "1.0.0").Returns(false);

        var result = await _handler.HandleAsync(new UploadPackageCommand(
            _tenantId, "MyCompany.Integrations", "1.0.0", "integrations.zip", data));

        Assert.Equal("production", result.SecretCheck.Environment);
        Assert.Empty(result.SecretCheck.Required);
        Assert.Empty(result.SecretCheck.Satisfied);
        Assert.Empty(result.SecretCheck.Missing);
        // No required secrets means we never need to read the environment's configured secrets.
        await _secretRepository.DidNotReceive().ListAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RequiredSecrets_ReportsSatisfiedAndMissingAgainstProduction()
    {
        var data = CreateZipWithDll();
        _repository.VersionExistsAsync(_tenantId, "MyCompany.Integrations", "1.0.0").Returns(false);
        _secretRepository.ListAsync(_tenantId, "production", Arg.Any<CancellationToken>())
            .Returns(new List<Secret> { new() { Key = "ERP_API_KEY", Environment = "production", TenantId = _tenantId } });

        var result = await _handler.HandleAsync(new UploadPackageCommand(
            _tenantId, "MyCompany.Integrations", "1.0.0", "integrations.zip", data,
            RequiredSecrets: ["ERP_API_KEY", "DB_CONNECTION_STRING"]));

        Assert.Equal("production", result.SecretCheck.Environment);
        Assert.Equal(["DB_CONNECTION_STRING", "ERP_API_KEY"], result.SecretCheck.Required);
        Assert.Equal(["ERP_API_KEY"], result.SecretCheck.Satisfied);
        Assert.Equal(["DB_CONNECTION_STRING"], result.SecretCheck.Missing);
    }

    [Fact]
    public async Task HandleAsync_ExplicitEnvironment_ProvisionsAndChecksSecretsAgainstIt()
    {
        var data = CreateZipWithDll();
        _repository.VersionExistsAsync(_tenantId, "MyCompany.Integrations", "1.0.0").Returns(false);
        _environmentRepository.ExistsAsync(_tenantId, "staging", Arg.Any<CancellationToken>()).Returns(true);
        _secretRepository.ListAsync(_tenantId, "staging", Arg.Any<CancellationToken>())
            .Returns(new List<Secret> { new() { Key = "ERP_API_KEY", Environment = "staging", TenantId = _tenantId } });
        _scanner.ScanZip(data).Returns([
            new DiscoveredIntegration(
                "Order Sync", "order-sync", "Acme.OrderSync",
                Description: null, TimeoutSeconds: null, RetryMaxAttempts: null, RetryBackoffSeconds: null,
                [new DiscoveredIntegrationTrigger("Hook", "hook", TriggerType.Webhook, CronExpression: null)])
        ]);

        var result = await _handler.HandleAsync(new UploadPackageCommand(
            _tenantId, "MyCompany.Integrations", "1.0.0", "integrations.zip", data,
            RequiredSecrets: ["ERP_API_KEY", "DB_CONNECTION_STRING"], Environment: "staging"));

        // Integration is provisioned into the requested environment, not the tenant default.
        await _integrationRepository.Received(1).UpsertBySlugAsync(
            Arg.Is<Integration>(i => i.Environment == "staging"),
            Arg.Any<IReadOnlyList<IntegrationTrigger>>(),
            Arg.Any<CancellationToken>());
        // And the secret check runs against the requested environment's secrets.
        Assert.Equal("staging", result.SecretCheck.Environment);
        Assert.Equal(["ERP_API_KEY"], result.SecretCheck.Satisfied);
        Assert.Equal(["DB_CONNECTION_STRING"], result.SecretCheck.Missing);
        await _environmentRepository.DidNotReceive().GetDefaultNameAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExplicitEnvironmentThatDoesNotExist_ThrowsAndCreatesNothing()
    {
        var data = CreateZipWithDll();
        _repository.VersionExistsAsync(_tenantId, "MyCompany.Integrations", "1.0.0").Returns(false);
        _environmentRepository.ExistsAsync(_tenantId, "staging", Arg.Any<CancellationToken>()).Returns(false);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _handler.HandleAsync(new UploadPackageCommand(
                _tenantId, "MyCompany.Integrations", "1.0.0", "integrations.zip", data,
                Environment: "staging")));

        Assert.Contains("staging", ex.Message);
        await _repository.DidNotReceive().CreateAsync(Arg.Any<AssemblyPackage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BlankEnvironment_FallsBackToTenantDefault()
    {
        var data = CreateZipWithDll();
        _repository.VersionExistsAsync(_tenantId, "MyCompany.Integrations", "1.0.0").Returns(false);

        // A whitespace-only environment is treated as "not supplied" and uses the tenant default.
        var result = await _handler.HandleAsync(new UploadPackageCommand(
            _tenantId, "MyCompany.Integrations", "1.0.0", "integrations.zip", data, Environment: "   "));

        Assert.Equal("production", result.SecretCheck.Environment);
        await _environmentRepository.Received(1).GetDefaultNameAsync(_tenantId, Arg.Any<CancellationToken>());
        await _environmentRepository.DidNotReceive().ExistsAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RequiredSecrets_MatchesConfiguredCaseInsensitivelyAndDedupes()
    {
        var data = CreateZipWithDll();
        _repository.VersionExistsAsync(_tenantId, "MyCompany.Integrations", "1.0.0").Returns(false);
        _secretRepository.ListAsync(_tenantId, "production", Arg.Any<CancellationToken>())
            .Returns(new List<Secret> { new() { Key = "ERP_API_KEY", Environment = "production", TenantId = _tenantId } });

        var result = await _handler.HandleAsync(new UploadPackageCommand(
            _tenantId, "MyCompany.Integrations", "1.0.0", "integrations.zip", data,
            RequiredSecrets: ["erp_api_key", "ERP_API_KEY", "  "]));

        Assert.Equal(["erp_api_key"], result.SecretCheck.Required);
        Assert.Equal(["erp_api_key"], result.SecretCheck.Satisfied);
        Assert.Empty(result.SecretCheck.Missing);
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
