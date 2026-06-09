using ControlPlane.Features.IntegrationPackages.Scanning;
using ControlPlane.Features.Integrations;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Integrations;

public class RepointIntegrationHandlerTests
{
    private readonly IIntegrationRepointRepository _repository = Substitute.For<IIntegrationRepointRepository>();
    private readonly IAssemblyScanner _scanner = Substitute.For<IAssemblyScanner>();
    private readonly RepointIntegrationHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();

    public RepointIntegrationHandlerTests() => _handler = new RepointIntegrationHandler(_repository, _scanner);

    private static AssemblyPackage Package(Guid id) =>
        new() { Id = id, Name = "MyCompany.Integrations", Version = "2.0.0", Data = [1, 2, 3] };

    private static DiscoveredIntegration Discovered(string className) =>
        new("Sync Orders", "sync-orders", className, null, null, null, null, []);

    [Fact]
    public async Task HandleAsync_PackageContainsClass_SetsPackageIdAndSaves()
    {
        var newPackageId = Guid.NewGuid();
        var integration = new Integration
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "Sync Orders",
            ClassName = "MyCompany.SyncOrders",
            PackageId = Guid.NewGuid()
        };

        _repository.GetByIdAsync(_tenantId, integration.Id).Returns(integration);
        _repository.GetPackageAsync(_tenantId, newPackageId).Returns(Package(newPackageId));
        _scanner.ScanZip(Arg.Any<byte[]>()).Returns([Discovered("MyCompany.SyncOrders")]);

        var result = await _handler.HandleAsync(new RepointIntegrationCommand(_tenantId, integration.Id, newPackageId));

        Assert.True(result);
        Assert.Equal(newPackageId, integration.PackageId);
        await _repository.Received(1).SaveAsync(integration);
    }

    [Fact]
    public async Task HandleAsync_PackageMissingClass_ThrowsValidationAndDoesNotSave()
    {
        var newPackageId = Guid.NewGuid();
        var integration = new Integration
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "Sync Orders",
            ClassName = "MyCompany.SyncOrders"
        };

        _repository.GetByIdAsync(_tenantId, integration.Id).Returns(integration);
        _repository.GetPackageAsync(_tenantId, newPackageId).Returns(Package(newPackageId));
        // The target package contains a different class.
        _scanner.ScanZip(Arg.Any<byte[]>()).Returns([Discovered("MyCompany.SomethingElse")]);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _handler.HandleAsync(new RepointIntegrationCommand(_tenantId, integration.Id, newPackageId)));

        await _repository.DidNotReceive().SaveAsync(Arg.Any<Integration>());
    }

    [Fact]
    public async Task HandleAsync_UnknownIntegration_Throws()
    {
        var id = Guid.NewGuid();
        _repository.GetByIdAsync(_tenantId, id).Returns((Integration?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.HandleAsync(new RepointIntegrationCommand(_tenantId, id, Guid.NewGuid())));
    }

    [Fact]
    public async Task HandleAsync_UnknownPackage_ThrowsAndDoesNotSave()
    {
        var unknownPackageId = Guid.NewGuid();
        var integration = new Integration
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Sync Orders", ClassName = "MyCompany.SyncOrders"
        };

        _repository.GetByIdAsync(_tenantId, integration.Id).Returns(integration);
        _repository.GetPackageAsync(_tenantId, unknownPackageId).Returns((AssemblyPackage?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.HandleAsync(new RepointIntegrationCommand(_tenantId, integration.Id, unknownPackageId)));

        await _repository.DidNotReceive().SaveAsync(Arg.Any<Integration>());
    }
}
