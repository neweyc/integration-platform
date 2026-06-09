using ControlPlane.Features.IntegrationPackages;
using ControlPlane.Features.IntegrationPackages.Scanning;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.IntegrationPackages;

public class ActivatePackageVersionHandlerTests
{
    private readonly IPackageActivationRepository _repository = Substitute.For<IPackageActivationRepository>();
    private readonly IAssemblyScanner _scanner = Substitute.For<IAssemblyScanner>();
    private readonly ActivatePackageVersionHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();

    public ActivatePackageVersionHandlerTests() => _handler = new ActivatePackageVersionHandler(_repository, _scanner);

    private static AssemblyPackage Package(Guid id) =>
        new() { Id = id, Name = "MyCompany.Integrations", Version = "2.0.0", Data = [1, 2, 3] };

    private static DiscoveredIntegration Discovered(string className) =>
        new("name", "slug", className, null, null, null, null, []);

    private static Integration Integration(string className, Guid? packageId) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = className,
            ClassName = className,
            PackageId = packageId
        };

    [Fact]
    public async Task HandleAsync_MovesEverySiblingToTheVersion()
    {
        var target = Guid.NewGuid();
        var oldVersion = Guid.NewGuid();
        var a = Integration("MyCompany.A", oldVersion);
        var b = Integration("MyCompany.B", oldVersion);

        _repository.GetPackageAsync(_tenantId, target).Returns(Package(target));
        _repository.ListIntegrationsForPackageNameAsync(_tenantId, "MyCompany.Integrations")
            .Returns([a, b]);
        _scanner.ScanZip(Arg.Any<byte[]>())
            .Returns([Discovered("MyCompany.A"), Discovered("MyCompany.B")]);

        var result = await _handler.HandleAsync(new ActivatePackageVersionCommand(_tenantId, target));

        Assert.Equal(target, a.PackageId);
        Assert.Equal(target, b.PackageId);
        Assert.Equal(2, result.Activated.Count);
        Assert.Empty(result.Skipped);
        await _repository.Received(1).SaveAsync();
    }

    [Fact]
    public async Task HandleAsync_LeavesSiblingWhoseClassIsAbsent_AndReportsItSkipped()
    {
        var target = Guid.NewGuid();
        var oldVersion = Guid.NewGuid();
        var stays = Integration("MyCompany.Removed", oldVersion);
        var moves = Integration("MyCompany.Kept", oldVersion);

        _repository.GetPackageAsync(_tenantId, target).Returns(Package(target));
        _repository.ListIntegrationsForPackageNameAsync(_tenantId, "MyCompany.Integrations")
            .Returns([stays, moves]);
        // The target version no longer contains MyCompany.Removed.
        _scanner.ScanZip(Arg.Any<byte[]>()).Returns([Discovered("MyCompany.Kept")]);

        var result = await _handler.HandleAsync(new ActivatePackageVersionCommand(_tenantId, target));

        Assert.Equal(target, moves.PackageId);
        Assert.Equal(oldVersion, stays.PackageId); // untouched — it cannot run from a version missing its class
        Assert.Equal(["MyCompany.Kept"], result.Activated);
        Assert.Equal(["MyCompany.Removed"], result.Skipped);
        await _repository.Received(1).SaveAsync();
    }

    [Fact]
    public async Task HandleAsync_CountsIntegrationsAlreadyOnTheVersionAsActivated()
    {
        var target = Guid.NewGuid();
        var already = Integration("MyCompany.A", target);

        _repository.GetPackageAsync(_tenantId, target).Returns(Package(target));
        _repository.ListIntegrationsForPackageNameAsync(_tenantId, "MyCompany.Integrations")
            .Returns([already]);
        _scanner.ScanZip(Arg.Any<byte[]>()).Returns([Discovered("MyCompany.A")]);

        var result = await _handler.HandleAsync(new ActivatePackageVersionCommand(_tenantId, target));

        Assert.Equal(["MyCompany.A"], result.Activated);
        Assert.Equal(target, already.PackageId);
    }

    [Fact]
    public async Task HandleAsync_UnknownPackage_ThrowsAndDoesNotSave()
    {
        var unknown = Guid.NewGuid();
        _repository.GetPackageAsync(_tenantId, unknown).Returns((AssemblyPackage?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.HandleAsync(new ActivatePackageVersionCommand(_tenantId, unknown)));

        await _repository.DidNotReceive().SaveAsync();
    }
}
