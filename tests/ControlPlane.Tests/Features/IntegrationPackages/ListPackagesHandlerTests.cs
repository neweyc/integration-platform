using ControlPlane.Features.IntegrationPackages;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.IntegrationPackages;

public class ListPackagesHandlerTests
{
    private readonly IPackageReadRepository _repository = Substitute.For<IPackageReadRepository>();
    private readonly ListPackagesHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();

    public ListPackagesHandlerTests()
    {
        _handler = new ListPackagesHandler(_repository);
    }

    [Fact]
    public async Task HandleAsync_ReturnsPackageMetadataWithoutData()
    {
        var packageId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        _repository.ListAsync(_tenantId).Returns([
            new AssemblyPackage
            {
                Id = packageId,
                TenantId = _tenantId,
                Name = "MyCompany.Integrations",
                Version = "1.0.0",
                FileName = "integrations.zip",
                Data = [1, 2, 3],
                SizeBytes = 3,
                Sha256Hash = "abc",
                CreatedAt = createdAt
            }
        ]);

        var result = await _handler.HandleAsync(new ListPackagesCommand(_tenantId));

        var package = Assert.Single(result.Packages);
        Assert.Equal(packageId, package.Id);
        Assert.Equal("MyCompany.Integrations", package.Name);
        Assert.Equal("1.0.0", package.Version);
        Assert.Equal(3, package.SizeBytes);
        Assert.Equal(createdAt, package.CreatedAt);
    }
}
