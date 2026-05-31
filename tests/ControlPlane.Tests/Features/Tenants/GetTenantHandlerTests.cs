using ControlPlane.Features.Tenants;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Tenants;

public class GetTenantHandlerTests
{
    private readonly ITenantReadRepository _repository = Substitute.For<ITenantReadRepository>();
    private readonly GetTenantHandler _handler;

    public GetTenantHandlerTests()
    {
        _handler = new GetTenantHandler(_repository);
    }

    [Fact]
    public async Task HandleAsync_ExistingTenant_ReturnsResult()
    {
        var id = Guid.NewGuid();
        var tenant = new Tenant { Id = id, Name = "Acme Corp", Slug = "acme-corp" };
        _repository.GetByIdAsync(id).Returns(tenant);

        var result = await _handler.HandleAsync(new GetTenantCommand(id));

        Assert.NotNull(result);
        Assert.Equal(id, result.Id);
        Assert.Equal("Acme Corp", result.Name);
        Assert.Equal("acme-corp", result.Slug);
    }

    [Fact]
    public async Task HandleAsync_NonExistentTenant_ReturnsNull()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>()).Returns((Tenant?)null);

        var result = await _handler.HandleAsync(new GetTenantCommand(Guid.NewGuid()));

        Assert.Null(result);
    }
}
