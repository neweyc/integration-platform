using ControlPlane.Features.AgentTokens;
using ControlPlane.Features.Billing;
using ControlPlane.Features.Environments;
using ControlPlane.Features.Integrations;
using ControlPlane.Features.Tenants;
using ControlPlane.Features.Workflows;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Integrations;

/// <summary>
/// Tests that package version pinning is stored, propagated to execution records,
/// and that history is immutable after repointing.
/// </summary>
public class PackagePinningTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly IEnvironmentReadRepository _environments = Substitute.For<IEnvironmentReadRepository>();
    private readonly ITenantReadRepository _tenants = Substitute.For<ITenantReadRepository>();
    private readonly BillingPlanCatalog _planCatalog = new(new StripeOptions());

    public PackagePinningTests()
    {
        _environments.ExistsAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _tenants.GetByIdAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Tenant { Id = _tenantId, Plan = BillingPlan.Free });
    }

    // CreateIntegration with package pin

    [Fact]
    public async Task CreateIntegration_WithValidPackagePin_StoresPackageId()
    {
        var repo = Substitute.For<IIntegrationRepository>();
        var handler = new CreateIntegrationHandler(repo, _encryption, _environments, _tenants, _planCatalog);
        var packageId = Guid.NewGuid();

        repo.SlugExistsAsync(_tenantId, "sync-orders").Returns(false);
        repo.PackageExistsAsync(_tenantId, packageId).Returns(true);
        repo.CreateAsync(Arg.Any<Integration>(), Arg.Any<IReadOnlyList<IntegrationTrigger>>())
            .Returns(call => call.Arg<Integration>());

        var result = await handler.HandleAsync(new CreateIntegrationCommand(
            _tenantId, "Sync Orders", "sync-orders", null,
            "production", "MyCompany.SyncOrders", [], PackageId: packageId));

        Assert.Equal(packageId, result.PackageId);
    }

    [Fact]
    public async Task CreateIntegration_WithUnknownPackage_Throws()
    {
        var repo = Substitute.For<IIntegrationRepository>();
        var handler = new CreateIntegrationHandler(repo, _encryption, _environments, _tenants, _planCatalog);
        var unknownId = Guid.NewGuid();

        repo.SlugExistsAsync(_tenantId, "sync-orders").Returns(false);
        repo.PackageExistsAsync(_tenantId, unknownId).Returns(false);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(
            new CreateIntegrationCommand(
                _tenantId, "Sync Orders", "sync-orders", null,
                "production", "MyCompany.SyncOrders", [], PackageId: unknownId)));
    }

    [Fact]
    public async Task CreateIntegration_WithNoPackage_PackageIdIsNull()
    {
        var repo = Substitute.For<IIntegrationRepository>();
        var handler = new CreateIntegrationHandler(repo, _encryption, _environments, _tenants, _planCatalog);

        repo.SlugExistsAsync(_tenantId, "sync-orders").Returns(false);
        repo.CreateAsync(Arg.Any<Integration>(), Arg.Any<IReadOnlyList<IntegrationTrigger>>())
            .Returns(call => call.Arg<Integration>());

        var result = await handler.HandleAsync(new CreateIntegrationCommand(
            _tenantId, "Sync Orders", "sync-orders", null,
            "production", "MyCompany.SyncOrders", []));

        Assert.Null(result.PackageId);
    }

    // UpdateIntegration repoints to a different package

    [Fact]
    public async Task UpdateIntegration_DoesNotChangePackagePin()
    {
        // A general edit (name/status/triggers) cannot change the active package version — the command
        // no longer carries a package id, so the only way to change the pin is the repoint endpoint.
        // This guards against the latent bug where the UI's update silently un-pinned the package.
        var repo = Substitute.For<IIntegrationUpdateRepository>();
        var handler = new UpdateIntegrationHandler(repo, _encryption);
        var pinnedPackageId = Guid.NewGuid();

        var integration = new Integration
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "Sync Orders",
            Slug = "sync-orders",
            Environment = "production",
            ClassName = "MyCompany.SyncOrders",
            PackageId = pinnedPackageId
        };

        repo.GetByIdAsync(_tenantId, integration.Id).Returns(integration);
        repo.UpdateAsync(Arg.Any<Integration>(), Arg.Any<IReadOnlyList<IntegrationTrigger>>())
            .Returns(call => call.Arg<Integration>());

        var result = await handler.HandleAsync(new UpdateIntegrationCommand(
            _tenantId, integration.Id, "Sync Orders", null,
            IntegrationStatus.Enabled, []));

        Assert.Equal(pinnedPackageId, result.PackageId);
    }

    // StartExecution snapshots package info

    [Fact]
    public async Task StartExecution_WithPinnedPackage_SnapshotsPackageInfoOnRecord()
    {
        var executionRepo = Substitute.For<IExecutionRepository>();
        var workItemRepo = Substitute.For<IWorkItemRepository>();
        var integrationRepo = Substitute.For<IIntegrationValidationRepository>();
        var manualRunRepo = Substitute.For<IManualRunRequestRepository>();
        var packageRepo = Substitute.For<IPackageLookupRepository>();
        var quotaService = Substitute.For<IQuotaService>();
        var workflowProgression = Substitute.For<IWorkflowProgressionService>();

        var handler = new StartExecutionHandler(
            executionRepo, workItemRepo, integrationRepo, manualRunRepo, packageRepo, quotaService, workflowProgression);

        var tenantId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        quotaService.HasAvailableExecutionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var workItem = new WorkItem
        {
            Id = workItemId,
            TenantId = tenantId,
            IntegrationId = integrationId,
            Environment = "production",
            TriggerSource = TriggerSource.Scheduled,
            Status = WorkItemStatus.Claimed,
            ClaimOwner = agentId,
            ClaimExpiresAt = now.AddMinutes(5)
        };

        var integration = new Integration
        {
            Id = integrationId,
            TenantId = tenantId,
            Name = "Sync Orders",
            Slug = "sync-orders",
            Environment = "production",
            Status = IntegrationStatus.Enabled,
            ClassName = "MyCompany.SyncOrders",
            PackageId = packageId
        };

        workItemRepo.GetByIdAsync(tenantId, workItemId).Returns(workItem);
        integrationRepo.GetByIdAsync(tenantId, integrationId).Returns(integration);
        executionRepo.HasRunningExecutionAsync(tenantId, integrationId).Returns(false);
        packageRepo.GetPackageInfoAsync(tenantId, packageId).Returns(("MyCompany.Integrations", "2.1.0"));
        executionRepo.CreateAsync(Arg.Any<ExecutionRecord>())
            .Returns(call => call.Arg<ExecutionRecord>());

        await handler.HandleAsync(
            new StartExecutionCommand(tenantId, "production", workItemId, agentId));

        await executionRepo.Received(1).CreateAsync(
            Arg.Is<ExecutionRecord>(r =>
                r.PackageId == packageId &&
                r.PackageName == "MyCompany.Integrations" &&
                r.PackageVersion == "2.1.0"));
    }

    [Fact]
    public async Task StartExecution_NoPinnedPackage_RecordHasNullPackageFields()
    {
        var executionRepo = Substitute.For<IExecutionRepository>();
        var workItemRepo = Substitute.For<IWorkItemRepository>();
        var integrationRepo = Substitute.For<IIntegrationValidationRepository>();
        var manualRunRepo = Substitute.For<IManualRunRequestRepository>();
        var packageRepo = Substitute.For<IPackageLookupRepository>();
        var quotaService = Substitute.For<IQuotaService>();
        var workflowProgression = Substitute.For<IWorkflowProgressionService>();

        var handler = new StartExecutionHandler(
            executionRepo, workItemRepo, integrationRepo, manualRunRepo, packageRepo, quotaService, workflowProgression);

        var tenantId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        quotaService.HasAvailableExecutionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var workItem = new WorkItem
        {
            Id = workItemId,
            TenantId = tenantId,
            IntegrationId = integrationId,
            Environment = "production",
            TriggerSource = TriggerSource.Scheduled,
            Status = WorkItemStatus.Claimed,
            ClaimOwner = agentId,
            ClaimExpiresAt = now.AddMinutes(5)
        };

        var integration = new Integration
        {
            Id = integrationId,
            TenantId = tenantId,
            Name = "Sync Orders",
            Slug = "sync-orders",
            Environment = "production",
            Status = IntegrationStatus.Enabled,
            ClassName = "MyCompany.SyncOrders",
            PackageId = null
        };

        workItemRepo.GetByIdAsync(tenantId, workItemId).Returns(workItem);
        integrationRepo.GetByIdAsync(tenantId, integrationId).Returns(integration);
        executionRepo.HasRunningExecutionAsync(tenantId, integrationId).Returns(false);
        executionRepo.CreateAsync(Arg.Any<ExecutionRecord>())
            .Returns(call => call.Arg<ExecutionRecord>());

        await handler.HandleAsync(
            new StartExecutionCommand(tenantId, "production", workItemId, agentId));

        await packageRepo.DidNotReceive().GetPackageInfoAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
        await executionRepo.Received(1).CreateAsync(
            Arg.Is<ExecutionRecord>(r => r.PackageId == null && r.PackageName == null));
    }
}
