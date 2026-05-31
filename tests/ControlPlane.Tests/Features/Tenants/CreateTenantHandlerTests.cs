using ControlPlane.Features.Tenants;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Tenants;

public class CreateTenantHandlerTests
{
    private readonly ITenantRepository _repository = Substitute.For<ITenantRepository>();
    private readonly CreateTenantHandler _handler;

    public CreateTenantHandlerTests()
    {
        _handler = new CreateTenantHandler(_repository);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_CreatesTenantAndReturnsResult()
    {
        var command = new CreateTenantCommand("Acme Corp", "acme-corp");

        _repository.SlugExistsAsync("acme-corp").Returns(false);
        _repository.CreateAsync(Arg.Any<Tenant>()).Returns(call =>
            call.Arg<Tenant>());

        var result = await _handler.HandleAsync(command);

        Assert.Equal("Acme Corp", result.Name);
        Assert.Equal("acme-corp", result.Slug);
        Assert.NotEqual(Guid.Empty, result.Id);
        await _repository.Received(1).CreateAsync(Arg.Is<Tenant>(t =>
            t.Name == "Acme Corp" && t.Slug == "acme-corp"));
    }

    [Theory]
    [InlineData("", "acme-corp", "Name is required.")]
    [InlineData("  ", "acme-corp", "Name is required.")]
    [InlineData("Acme", "", "Slug is required.")]
    [InlineData("Acme", "  ", "Slug is required.")]
    [InlineData("Acme", "Acme Corp", "Slug may only contain lowercase letters, numbers, and hyphens.")]
    [InlineData("Acme", "acme_corp", "Slug may only contain lowercase letters, numbers, and hyphens.")]
    public async Task HandleAsync_InvalidInput_ThrowsValidationException(string name, string slug, string expectedMessage)
    {
        var command = new CreateTenantCommand(name, slug);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command));

        Assert.Equal(expectedMessage, ex.Message);
    }

    [Fact]
    public async Task HandleAsync_DuplicateSlug_ThrowsConflictException()
    {
        var command = new CreateTenantCommand("Acme Corp", "acme-corp");
        _repository.SlugExistsAsync("acme-corp").Returns(true);

        await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(command));
    }
}
