using System.Reflection;
using IntegrationPlatform.Sdk;

namespace RuntimeAgent.Execution;

// Scans a directory for .dll files and finds all IIntegration implementations.
// Assemblies are loaded once and cached — restart the agent to pick up new assemblies.
public class IntegrationLoader(ILogger<IntegrationLoader> logger)
{
    private readonly Dictionary<string, Type> _integrationTypes = new();
    private bool _loaded;

    public void LoadFromDirectory(string path)
    {
        if (_loaded) return;
        _loaded = true;

        if (!Directory.Exists(path))
        {
            logger.LogWarning("Integrations path does not exist: {Path}", path);
            return;
        }

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
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load assembly: {Dll}", dll);
            }
        }

        logger.LogInformation("Loaded {Count} integration(s) from {Path}", _integrationTypes.Count, path);
    }

    public IIntegration? Resolve(string slug)
    {
        // Normalise the slug to a bare word for comparison:
        // "hello-world" → "helloworld", "HelloWorldIntegration" → "helloworldintegration"
        var normalised = slug.Replace("-", "").Replace("_", "");

        var type = _integrationTypes.Values.FirstOrDefault(t =>
            string.Equals(Normalise(t.Name), normalised, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Normalise(t.FullName!), normalised, StringComparison.OrdinalIgnoreCase));

        if (type is null)
        {
            logger.LogWarning("No integration found for slug '{Slug}' (normalised: '{Normalised}'). " +
                              "Loaded types: {Types}",
                              slug, normalised, string.Join(", ", _integrationTypes.Keys));
            return null;
        }

        return (IIntegration)Activator.CreateInstance(type)!;
    }

    private static string Normalise(string name) => name.Replace("-", "").Replace("_", "");
}
