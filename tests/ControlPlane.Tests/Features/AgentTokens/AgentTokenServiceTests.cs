using ControlPlane.Features.AgentTokens;

namespace ControlPlane.Tests.Features.AgentTokens;

public class AgentTokenServiceTests
{
    private readonly AgentTokenService _service = new();

    [Fact]
    public void Generate_ReturnsTokenWithCorrectPrefix()
    {
        var token = _service.Generate();
        
        Assert.StartsWith("agt_", token);
        // "agt_" (4) + 32 bytes base64url encoded (~43 chars)
        Assert.True(token.Length > 40);
    }

    [Fact]
    public void Hash_ReturnsConsistentLowercaseHex()
    {
        var token = "agt_test_token_123";
        
        var hash1 = _service.Hash(token);
        var hash2 = _service.Hash(token);
        
        Assert.Equal(hash1, hash2);
        Assert.Matches("^[a-f0-9]{64}$", hash1);
    }

    [Fact]
    public void Hash_DifferentTokens_ProduceDifferentHashes()
    {
        var hash1 = _service.Hash("agt_token1");
        var hash2 = _service.Hash("agt_token2");
        
        Assert.NotEqual(hash1, hash2);
    }
}
