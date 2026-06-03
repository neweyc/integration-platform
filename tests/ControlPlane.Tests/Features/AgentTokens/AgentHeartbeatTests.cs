using ControlPlane.Features.AgentTokens;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.AgentTokens;

public class AgentHeartbeatTests
{
    [Fact]
    public async Task HandleAsync_ValidHeartbeat_UpsertsHeartbeat()
    {
        var repository = Substitute.For<IAgentHeartbeatRepository>();
        var handler = new AgentHeartbeatHandler(repository);
        var tenantId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();

        await handler.HandleAsync(new AgentHeartbeatCommand(
            tenantId,
            tokenId,
            "production",
            "1.2.3",
            "host-a",
            CurrentConcurrency: 2,
            MaxConcurrency: 5));

        await repository.Received(1).UpsertAsync(Arg.Is<AgentHeartbeat>(h =>
            h.TenantId == tenantId
            && h.AgentTokenId == tokenId
            && h.Environment == "production"
            && h.Version == "1.2.3"
            && h.Hostname == "host-a"
            && h.CurrentConcurrency == 2
            && h.MaxConcurrency == 5));
    }

    [Fact]
    public async Task ListAsync_StaleHeartbeat_IsMarkedStale()
    {
        var repository = Substitute.For<IAgentHeartbeatRepository>();
        var handler = new ListAgentHeartbeatsHandler(repository);
        var tenantId = Guid.NewGuid();

        repository.ListAsync(tenantId).Returns([
            new AgentHeartbeat
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                AgentTokenId = Guid.NewGuid(),
                Environment = "production",
                LastSeenAt = DateTime.UtcNow.AddMinutes(-5),
                MaxConcurrency = 5
            }
        ]);

        var result = await handler.HandleAsync(new ListAgentHeartbeatsCommand(tenantId));

        var agent = Assert.Single(result.Agents);
        Assert.True(agent.IsStale);
    }
}
