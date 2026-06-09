using Microsoft.Extensions.Logging.Abstractions;
using RuntimeAgent.Execution;
using Serto.Sdk;

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

        // The class loads into its own package context, so the instance is intentionally a distinct
        // Type identity from the compile-time type. It must still satisfy the shared IIntegration
        // contract (which only holds when the SDK assembly is shared with the default context) and
        // expose the same fully-qualified name.
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IIntegration>(result);
        Assert.Equal(typeof(SuccessfulTestIntegration).FullName, result.GetType().FullName);
    }

    [Fact]
    public void ResolveFromDirectory_ClassNotInPackageDir_ReturnsNullInsteadOfStaleGlobal()
    {
        // A pinned integration must never silently run a different version from the global pool.
        // Even though the class is loaded globally (from BaseDirectory), resolving against a package
        // directory that does not contain it must return null so the run is skipped, not run stale.
        var loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);
        loader.LoadFromDirectory(AppContext.BaseDirectory);

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = loader.ResolveFromDirectory(
                typeof(SuccessfulTestIntegration).FullName!,
                tempDir);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    [Fact]
    public void LoadPackage_TwoDirectoriesSameClass_ProduceIsolatedTypeIdentities()
    {
        // The crux of the repoint fix: the same class loaded from two package directories must produce
        // two distinct Type identities so both versions can coexist in one process. Without isolation
        // the runtime would dedupe by assembly identity and the first-loaded version would win.
        var loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);

        var dirA = CopyBaseAssembliesToTempDir();
        var dirB = CopyBaseAssembliesToTempDir();
        try
        {
            var a = loader.ResolveFromDirectory(typeof(SuccessfulTestIntegration).FullName!, dirA);
            var b = loader.ResolveFromDirectory(typeof(SuccessfulTestIntegration).FullName!, dirB);

            Assert.NotNull(a);
            Assert.NotNull(b);
            // Same logical class…
            Assert.Equal(a!.GetType().FullName, b!.GetType().FullName);
            // …but loaded into separate contexts, so the Type identities are distinct and coexist.
            Assert.NotSame(a.GetType(), b.GetType());
            // Both still satisfy the shared contract (only true when the SDK assembly is shared).
            Assert.IsAssignableFrom<IIntegration>(a);
            Assert.IsAssignableFrom<IIntegration>(b);
        }
        finally
        {
            Directory.Delete(dirA, recursive: true);
            Directory.Delete(dirB, recursive: true);
        }
    }

    private static string CopyBaseAssembliesToTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        foreach (var dll in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
            File.Copy(dll, Path.Combine(dir, Path.GetFileName(dll)), overwrite: true);
        return dir;
    }

    [Fact]
    public void LoadFromDirectory_MissingDirThenCreated_LoadsAfterPackageLands()
    {
        // Reproduces the repoint bug: an execution reaches a pinned package directory before it is
        // synced. The missing directory must NOT be recorded as loaded, so once the package lands the
        // real assembly is loaded and the new version runs — rather than being skipped forever.
        var loader = new IntegrationLoader(NullLogger<IntegrationLoader>.Instance);

        var packageDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            // First touch: directory does not exist yet (package not synced). Must return null without
            // poisoning the path so it can still be loaded once the package lands.
            Assert.Null(loader.ResolveFromDirectory(typeof(SuccessfulTestIntegration).FullName!, packageDir));

            // The package "arrives": copy the loadable assemblies into the directory.
            Directory.CreateDirectory(packageDir);
            foreach (var dll in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
                File.Copy(dll, Path.Combine(packageDir, Path.GetFileName(dll)), overwrite: true);

            // A fresh loader never primed its global pool, so a non-null result can only come from the
            // package directory actually being loaded now (i.e. the earlier missing call did not poison it).
            var result = loader.ResolveFromDirectory(typeof(SuccessfulTestIntegration).FullName!, packageDir);

            Assert.NotNull(result);
        }
        finally
        {
            if (Directory.Exists(packageDir))
                Directory.Delete(packageDir, recursive: true);
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
