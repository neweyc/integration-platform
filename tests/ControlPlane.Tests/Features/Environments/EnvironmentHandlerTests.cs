using ControlPlane.Features.Environments;
using ControlPlane.Infrastructure;
using NSubstitute;
using Environment = Shared.Domain.Environment;

namespace ControlPlane.Tests.Features.Environments;

public class EnvironmentHandlerTests
{
    private readonly IEnvironmentWriteRepository _repository = Substitute.For<IEnvironmentWriteRepository>();
    private readonly Guid _tenantId = Guid.NewGuid();

    private CreateEnvironmentHandler CreateHandler => new(_repository);
    private UpdateEnvironmentHandler UpdateHandler => new(_repository);
    private DeleteEnvironmentHandler DeleteHandler => new(_repository);

    [Fact]
    public async Task Create_NormalizesNameAndPersists()
    {
        _repository.ExistsAsync(_tenantId, "staging", Arg.Any<CancellationToken>()).Returns(false);
        _repository.ListTrackedAsync(_tenantId, Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateHandler.HandleAsync(
            new CreateEnvironmentCommand(_tenantId, "  Staging ", "Staging", null, 1, false));

        Assert.Equal("staging", result.Name);
        await _repository.Received(1).AddAsync(
            Arg.Is<Environment>(e => e.Name == "staging" && e.TenantId == _tenantId), Arg.Any<CancellationToken>());
        await _repository.Received(1).SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_DuplicateName_ThrowsConflict()
    {
        _repository.ExistsAsync(_tenantId, "production", Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<ConflictException>(() => CreateHandler.HandleAsync(
            new CreateEnvironmentCommand(_tenantId, "Production", null, null, 0, false)));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("UPPER!")]
    [InlineData("")]
    public async Task Create_InvalidName_ThrowsValidation(string name)
    {
        await Assert.ThrowsAsync<ValidationException>(() => CreateHandler.HandleAsync(
            new CreateEnvironmentCommand(_tenantId, name, null, null, 0, false)));
    }

    [Fact]
    public async Task Delete_InUseEnvironment_ThrowsConflictListingDependents()
    {
        _repository.FindAsync(_tenantId, "production", Arg.Any<CancellationToken>())
            .Returns(new Environment { TenantId = _tenantId, Name = "production" });
        _repository.GetUsageAsync(_tenantId, "production", Arg.Any<CancellationToken>())
            .Returns(new EnvironmentUsage(Secrets: 2, Integrations: 1, AgentTokens: 0, Workflows: 0));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => DeleteHandler.HandleAsync(
            new DeleteEnvironmentCommand(_tenantId, "production")));

        Assert.Contains("integration", ex.Message);
        Assert.Contains("secret", ex.Message);
    }

    [Fact]
    public async Task Delete_UnusedEnvironment_Removes()
    {
        var environment = new Environment { TenantId = _tenantId, Name = "staging" };
        _repository.FindAsync(_tenantId, "staging", Arg.Any<CancellationToken>()).Returns(environment);
        _repository.GetUsageAsync(_tenantId, "staging", Arg.Any<CancellationToken>())
            .Returns(new EnvironmentUsage(0, 0, 0, 0));

        var result = await DeleteHandler.HandleAsync(new DeleteEnvironmentCommand(_tenantId, "staging"));

        Assert.True(result);
        _repository.Received(1).Remove(environment);
        await _repository.Received(1).SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_UnknownEnvironment_ThrowsNotFound()
    {
        _repository.FindAsync(_tenantId, "ghost", Arg.Any<CancellationToken>()).Returns((Environment?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => DeleteHandler.HandleAsync(
            new DeleteEnvironmentCommand(_tenantId, "ghost")));
    }

    [Fact]
    public async Task Delete_DefaultEnvironment_ThrowsConflict()
    {
        // The default must always exist (package auto-provisioning targets it).
        _repository.FindAsync(_tenantId, "production", Arg.Any<CancellationToken>())
            .Returns(new Environment { TenantId = _tenantId, Name = "production", IsDefault = true });

        await Assert.ThrowsAsync<ConflictException>(() => DeleteHandler.HandleAsync(
            new DeleteEnvironmentCommand(_tenantId, "production")));
        await _repository.DidNotReceive().GetUsageAsync(_tenantId, "production", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_ClearingDefaultFlag_ThrowsValidation()
    {
        _repository.FindAsync(_tenantId, "production", Arg.Any<CancellationToken>())
            .Returns(new Environment { TenantId = _tenantId, Name = "production", IsDefault = true });

        await Assert.ThrowsAsync<ValidationException>(() => UpdateHandler.HandleAsync(
            new UpdateEnvironmentCommand(_tenantId, "production", "Production", null, 0, IsDefault: false)));
    }
}
