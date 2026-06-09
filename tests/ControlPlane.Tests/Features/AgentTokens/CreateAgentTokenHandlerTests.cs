using ControlPlane.Features.AgentTokens;
using ControlPlane.Features.Environments;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.AgentTokens;

public class CreateAgentTokenHandlerTests
{
    private readonly IAgentTokenRepository _repository = Substitute.For<IAgentTokenRepository>();
    private readonly IAgentTokenService _tokenService = Substitute.For<IAgentTokenService>();
    private readonly IEnvironmentReadRepository _environments = Substitute.For<IEnvironmentReadRepository>();
    private readonly CreateAgentTokenHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();

    public CreateAgentTokenHandlerTests()
    {
        _handler = new CreateAgentTokenHandler(_repository, _tokenService, _environments);
        _environments.ExistsAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _repository.CreateAsync(Arg.Any<AgentToken>()).Returns(call => call.Arg<AgentToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_CreatesAndReturnsTokenWithPlaintext()
    {
        var command = new CreateAgentTokenCommand(_tenantId, "Prod Agent", "production");
        _tokenService.Generate().Returns("agt_plaintext");
        _tokenService.Hash("agt_plaintext").Returns("hashed_value");

        var result = await _handler.HandleAsync(command);

        Assert.Equal("Prod Agent", result.Name);
        Assert.Equal("production", result.Environment);
        Assert.Equal("agt_plaintext", result.Token);
        
        await _repository.Received(1).CreateAsync(Arg.Is<AgentToken>(t => 
            t.TenantId == _tenantId &&
            t.Name == "Prod Agent" &&
            t.Environment == "production" &&
            t.TokenHash == "hashed_value"
        ));
    }

    [Theory]
    [InlineData("", "production", "Name is required.")]
    [InlineData("Name", "", "Environment is required.")]
    public async Task HandleAsync_InvalidInput_ThrowsValidationException(
        string name, string environment, string expectedMessage)
    {
        var command = new CreateAgentTokenCommand(_tenantId, name, environment);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));

        Assert.Equal(expectedMessage, ex.Message);
    }
}
