using ControlPlane.Features.AgentTokens;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.AgentTokens;

public class RevokeAgentTokenHandlerTests
{
    private readonly IAgentTokenDeleteRepository _repository = Substitute.For<IAgentTokenDeleteRepository>();
    private readonly RevokeAgentTokenHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _tokenId = Guid.NewGuid();

    public RevokeAgentTokenHandlerTests()
    {
        _handler = new RevokeAgentTokenHandler(_repository);
    }

    [Fact]
    public async Task HandleAsync_ExistingToken_DeletesAndReturnsTrue()
    {
        var token = new AgentToken { Id = _tokenId, TenantId = _tenantId };
        _repository.FindAsync(_tenantId, _tokenId).Returns(token);

        var result = await _handler.HandleAsync(new RevokeAgentTokenCommand(_tenantId, _tokenId));

        Assert.True(result);
        await _repository.Received(1).DeleteAsync(token);
    }

    [Fact]
    public async Task HandleAsync_NonExistentToken_ThrowsNotFoundException()
    {
        _repository.FindAsync(_tenantId, _tokenId).Returns((AgentToken?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => 
            _handler.HandleAsync(new RevokeAgentTokenCommand(_tenantId, _tokenId)));
    }
}
