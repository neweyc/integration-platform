using ControlPlane.Features.Secrets;
using ControlPlane.Infrastructure;
using NSubstitute;

namespace ControlPlane.Tests.Features.Secrets;

public class DeleteSecretHandlerTests
{
    private readonly ISecretDeleteRepository _repository = Substitute.For<ISecretDeleteRepository>();
    private readonly DeleteSecretHandler _handler;

    private readonly Guid _tenantId = Guid.NewGuid();

    public DeleteSecretHandlerTests()
    {
        _handler = new DeleteSecretHandler(_repository);
    }

    [Fact]
    public async Task HandleAsync_ExistingSecret_DeletesAndReturnsTrue()
    {
        _repository.DeleteAsync(_tenantId, "production", "API_KEY").Returns(true);

        var result = await _handler.HandleAsync(new DeleteSecretCommand(_tenantId, "production", "API_KEY"));

        Assert.True(result);
    }

    [Fact]
    public async Task HandleAsync_NonExistentSecret_ThrowsNotFoundException()
    {
        _repository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.HandleAsync(new DeleteSecretCommand(_tenantId, "production", "MISSING_KEY")));
    }
}
