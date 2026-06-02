using System.Collections.Concurrent;
using System.Reflection;
using IntegrationPlatform.Sdk;

namespace RuntimeAgent.Execution;

// Scans directories for .dll files and finds all IIntegration implementations.
// Can be called multiple times with different paths (e.g. once per synced package).
// Thread-safe: Resolve may be called concurrently while LoadFromDirectory is running.
public class IntegrationLoader(ILogger<IntegrationLoader> logger)
{
    private readonly ConcurrentDictionary<string, Type> _integrationTypes = new();
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

    public IIntegration? Resolve(string className)
    {
        if (_integrationTypes.TryGetValue(className, out var type))
            return (IIntegration)Activator.CreateInstance(type)!;

        logger.LogWarning("No integration found for class name '{ClassName}'. Loaded types: {Types}",
            className, string.Join(", ", _integrationTypes.Keys));
        return null;
    }
}
