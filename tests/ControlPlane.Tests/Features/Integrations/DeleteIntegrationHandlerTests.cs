using ControlPlane.Features.Integrations;
using ControlPlane.Infrastructure;
using NSubstitute;

namespace ControlPlane.Tests.Features.Integrations;

public class DeleteIntegrationHandlerTests
{
    private readonly IIntegrationDeleteRepository _repository = Substitute.For<IIntegrationDeleteRepository>();
    private readonly DeleteIntegrationHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _integrationId = Guid.NewGuid();

    public DeleteIntegrationHandlerTests()
    {
        _handler = new DeleteIntegrationHandler(_repository);
    }

    [Fact]
    public async Task HandleAsync_ExistingIntegration_DeletesAndReturnsTrue()
    {
        _repository.DeleteAsync(_tenantId, _integrationId).Returns(true);

        var result = await _handler.HandleAsync(new DeleteIntegrationCommand(_tenantId, _integrationId));

        Assert.True(result);
    }

    [Fact]
    public async Task HandleAsync_NonExistentIntegration_ThrowsNotFoundException()
    {
        _repository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(false);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.HandleAsync(new DeleteIntegrationCommand(_tenantId, _integrationId)));
    }
}
