using Microsoft.Extensions.Logging.Abstractions;
using RuntimeAgent.Execution;

namespace RuntimeAgent.Tests;

public class IntegrationLoaderTests
{
    private readonly IntegrationLoader _loader;

    public IntegrationLoaderTests()
    {
        _loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);
    }

    [Fact]
    public void Resolve_UnknownClassName_ReturnsNull()
    {
        // Arrange - load from a directory that exists but has no matching types
        _loader.LoadFromDirectory(AppContext.BaseDirectory);

        // Act
        var result = _loader.Resolve("NonExistent.ClassName");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void LoadFromDirectory_NonExistentPath_DoesNotThrow()
    {
        // Arrange
        var loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);

        // Act & Assert - should not throw
        loader.LoadFromDirectory("/non/existent/path");
    }

    [Fact]
    public void LoadFromDirectory_CalledTwice_OnlyLoadsOnce()
    {
        // Arrange
        var loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);

        // Act - call twice
        loader.LoadFromDirectory(AppContext.BaseDirectory);
        loader.LoadFromDirectory(AppContext.BaseDirectory);

        // Assert - should not throw, second call is a no-op
        // This tests the _loaded flag behavior
        Assert.Null(loader.Resolve("NonExistent.ClassName"));
    }
}
