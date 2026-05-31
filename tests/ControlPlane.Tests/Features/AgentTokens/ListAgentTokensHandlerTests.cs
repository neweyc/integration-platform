using ControlPlane.Features.AgentTokens;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.AgentTokens;

public class ListAgentTokensHandlerTests
{
    private readonly IAgentTokenReadRepository _repository = Substitute.For<IAgentTokenReadRepository>();
    private readonly ListAgentTokensHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();

    public ListAgentTokensHandlerTests()
    {
        _handler = new ListAgentTokensHandler(_repository);
    }

    [Fact]
    public async Task HandleAsync_ReturnsTokensForTenant()
    {
        var tokens = new List<AgentToken>
        {
            new() { Id = Guid.NewGuid(), Name = "Token 1", Environment = "prod", TenantId = _tenantId, CreatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), Name = "Token 2", Environment = "dev", TenantId = _tenantId, CreatedAt = DateTime.UtcNow }
        };

        _repository.ListAsync(_tenantId).Returns(tokens);

        var result = await _handler.HandleAsync(new ListAgentTokensCommand(_tenantId));

        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal("Token 1", result.Tokens[0].Name);
        Assert.Equal("Token 2", result.Tokens[1].Name);
    }
}
