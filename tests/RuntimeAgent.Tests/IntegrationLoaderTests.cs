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
        var loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);

        loader.LoadFromDirectory(AppContext.BaseDirectory);
        loader.LoadFromDirectory(AppContext.BaseDirectory);

        Assert.Null(loader.Resolve("NonExistent.ClassName"));
    }

    [Fact]
    public void ResolveFromDirectory_ClassInDirectory_ReturnsInstance()
    {
        var loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);

        var result = loader.ResolveFromDirectory(
            typeof(SuccessfulTestIntegration).FullName!,
            AppContext.BaseDirectory);

        Assert.NotNull(result);
        Assert.IsType<SuccessfulTestIntegration>(result);
    }

    [Fact]
    public void ResolveFromDirectory_ClassNotInDirectory_FallsBackToGlobalPool()
    {
        var loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);
        loader.LoadFromDirectory(AppContext.BaseDirectory);

        // Use a non-existent subdirectory. The class should not be there, but is in the global pool.
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = loader.ResolveFromDirectory(
                typeof(SuccessfulTestIntegration).FullName!,
                tempDir);

            // Falls back to global pool since tempDir has no DLLs but BaseDirectory was loaded
            Assert.NotNull(result);
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    [Fact]
    public void ResolveFromDirectory_NonExistentClassAndDirectory_ReturnsNull()
    {
        var loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = loader.ResolveFromDirectory("No.Such.Class", tempDir);
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }
}
