using ControlPlane.Features.IntegrationPackages;
using ControlPlane.Infrastructure;
using NSubstitute;

namespace ControlPlane.Tests.Features.IntegrationPackages;

public class DeletePackageHandlerTests
{
    private readonly IPackageDeleteRepository _repository = Substitute.For<IPackageDeleteRepository>();
    private readonly DeletePackageHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _packageId = Guid.NewGuid();

    public DeletePackageHandlerTests() => _handler = new DeletePackageHandler(_repository);

    [Fact]
    public async Task HandleAsync_PackageNotPinned_Deletes()
    {
        _repository.ListPinnedIntegrationNamesAsync(_tenantId, _packageId).Returns([]);
        _repository.DeleteAsync(_tenantId, _packageId).Returns(true);

        var result = await _handler.HandleAsync(new DeletePackageCommand(_tenantId, _packageId));

        Assert.True(result);
        await _repository.Received(1).DeleteAsync(_tenantId, _packageId);
    }

    [Fact]
    public async Task HandleAsync_PackagePinnedToIntegration_ThrowsConflictAndDoesNotDelete()
    {
        _repository.ListPinnedIntegrationNamesAsync(_tenantId, _packageId)
            .Returns(["Sync Orders", "Nightly Report"]);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _handler.HandleAsync(new DeletePackageCommand(_tenantId, _packageId)));

        Assert.Contains("Sync Orders", ex.Message);
        Assert.Contains("Nightly Report", ex.Message);
        await _repository.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
    }
}
