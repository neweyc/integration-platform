using System.Collections.Concurrent;
using System.Reflection;
using Serto.Sdk;

namespace RuntimeAgent.Execution;

// Scans directories for .dll files and finds all IIntegration implementations.
// Can be called multiple times with different paths (e.g. once per synced package).
// Thread-safe: Resolve may be called concurrently while LoadFromDirectory is running.
public class IntegrationLoader(ILogger<IntegrationLoader> logger)
{
    // Global pool: className -> most-recently-loaded Type (fallback)
    private readonly ConcurrentDictionary<string, Type> _integrationTypes = new();

    // Per-path pool: resolvedPath -> (className -> Type)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Type>> _typesByPath = new();

    private readonly ConcurrentDictionary<string, bool> _loadedPaths = new();

    public void LoadFromDirectory(string path)
    {
        var resolved = Path.GetFullPath(path);

        if (!_loadedPaths.TryAdd(resolved, true))
            return;

        if (!Directory.Exists(path))
        {
            logger.LogWarning("Integrations path does not exist: {Path}", path);
            return;
        }

        var pathTypes = new ConcurrentDictionary<string, Type>();
        _typesByPath[resolved] = pathTypes;

        var found = 0;
        foreach (var dll in Directory.EnumerateFiles(path, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                var types = assembly.GetTypes()
                    .Where(t => typeof(IIntegration).IsAssignableFrom(t) && !t.IsAbstract && t.IsPublic);

                foreach (var type in types)
                {
                    _integrationTypes[type.FullName!] = type;
                    pathTypes[type.FullName!] = type;
                    logger.LogInformation("Loaded integration: {TypeName}", type.FullName);
                    found++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load assembly: {Dll}", dll);
            }
        }

        logger.LogInformation("Loaded {Count} integration(s) from {Path}", found, path);
    }

    // Resolves from a specific directory first, then falls back to the global pool.
    // Used when an integration is pinned to a specific package version.
    public IIntegration? ResolveFromDirectory(string className, string directory)
    {
        var resolved = Path.GetFullPath(directory);
        LoadFromDirectory(directory);

        if (_typesByPath.TryGetValue(resolved, out var pathTypes)
            && pathTypes.TryGetValue(className, out var pinned))
        {
            return (IIntegration)Activator.CreateInstance(pinned)!;
        }

        logger.LogWarning(
            "Class '{ClassName}' not found in package directory {Dir}; falling back to global pool",
            className, directory);

        return Resolve(className);
    }

    public IIntegration? Resolve(string className)
    {
        if (_integrationTypes.TryGetValue(className, out var type))
            return (IIntegration)Activator.CreateInstance(type)!;

        logger.LogWarning("No integration found for class name '{ClassName}'. Loaded types: {Types}",
            className, string.Join(", ", _integrationTypes.Keys));
        return null;
    }
}
