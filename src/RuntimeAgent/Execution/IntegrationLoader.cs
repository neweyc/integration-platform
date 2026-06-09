using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Serto.Sdk;

namespace RuntimeAgent.Execution;

// Scans directories for .dll files and finds all IIntegration implementations.
//
// Two load modes:
//   • LoadFromDirectory — the agent's local IntegrationsPath. A single, static set of assemblies for
//     dev/simple deployments, loaded into the default context.
//   • LoadPackage — a versioned, downloaded package directory. Loaded into its own
//     AssemblyLoadContext so that two package versions sharing the same assembly identity (the
//     default AssemblyVersion 1.0.0.0 is rarely bumped per build) can coexist in one process. Without
//     this, the runtime dedupes by identity and the first-loaded version wins, so repointing an
//     integration to a new package would never take effect until the agent restarted. The Serto.Sdk
//     contract is shared with the default context so IIntegration/IIntegrationContext/ILogger
//     identities match across the agent/integration boundary.
//
// Thread-safe: Resolve may be called concurrently while a load is running.
public class IntegrationLoader(ILogger<IntegrationLoader> logger)
{
    // Global pool: className -> most-recently-loaded Type. Populated only by local (non-isolated)
    // loads and used only for non-pinned resolution; packages resolve through _typesByPath.
    private readonly ConcurrentDictionary<string, Type> _integrationTypes = new();

    // Per-path pool: resolvedPath -> (className -> Type). The source of truth for pinned packages.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Type>> _typesByPath = new();

    private readonly ConcurrentDictionary<string, bool> _loadedPaths = new();

    // Loads the agent's local integrations directory into the default context.
    public void LoadFromDirectory(string path) => Load(path, isolate: false);

    // Loads a downloaded package directory into its own isolated context.
    public void LoadPackage(string packageDirectory) => Load(packageDirectory, isolate: true);

    private void Load(string path, bool isolate)
    {
        var resolved = Path.GetFullPath(path);

        // Check existence BEFORE recording the path as loaded. A pinned package's directory may not
        // be synced yet when an execution first reaches it; marking it loaded here would permanently
        // skip the real load once the package lands, so the agent would keep running the old version.
        if (!Directory.Exists(path))
        {
            logger.LogWarning("Integrations path does not exist yet: {Path}", path);
            return;
        }

        if (!_loadedPaths.TryAdd(resolved, true))
            return;

        var context = isolate ? new PackageLoadContext(resolved) : null;
        var pathTypes = new ConcurrentDictionary<string, Type>();
        _typesByPath[resolved] = pathTypes;

        var found = 0;
        foreach (var dll in Directory.EnumerateFiles(path, "*.dll"))
        {
            AssemblyName assemblyName;
            try
            {
                assemblyName = AssemblyName.GetAssemblyName(dll);
            }
            catch
            {
                // Native or otherwise non-managed dll — nothing to scan.
                continue;
            }

            // In an isolated package, never load the shared contract assemblies into the plugin
            // context: their types must keep the same identity as the agent's, so they resolve from
            // the default context instead.
            if (isolate && PackageLoadContext.IsSharedContract(assemblyName.Name))
                continue;

            try
            {
                var assembly = isolate
                    ? context!.LoadFromAssemblyPath(dll)
                    : Assembly.LoadFrom(dll);

                var types = GetLoadableTypes(assembly)
                    .Where(t => typeof(IIntegration).IsAssignableFrom(t) && !t.IsAbstract && t.IsPublic);

                foreach (var type in types)
                {
                    pathTypes[type.FullName!] = type;

                    // Only local loads feed the global pool; isolated package types stay per-path so a
                    // newer package version can never leak into another integration's resolution.
                    if (!isolate)
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

    // Resolves a class from a specific package directory. Used when an integration is pinned to a
    // package version. Deliberately does NOT fall back to the global pool: a pinned integration whose
    // package is not yet synced must not silently run a different (older) version. Returning null
    // skips the attempt so the work item is retried once the package is available.
    public IIntegration? ResolveFromDirectory(string className, string directory)
    {
        var resolved = Path.GetFullPath(directory);
        LoadPackage(directory);

        if (_typesByPath.TryGetValue(resolved, out var pathTypes)
            && pathTypes.TryGetValue(className, out var pinned))
        {
            return (IIntegration)Activator.CreateInstance(pinned)!;
        }

        logger.LogWarning(
            "Class '{ClassName}' not yet available in package directory {Dir} — skipping until the package is synced",
            className, directory);

        return null;
    }

    public IIntegration? Resolve(string className)
    {
        if (_integrationTypes.TryGetValue(className, out var type))
            return (IIntegration)Activator.CreateInstance(type)!;

        logger.LogWarning("No integration found for class name '{ClassName}'. Loaded types: {Types}",
            className, string.Join(", ", _integrationTypes.Keys));
        return null;
    }

    // Tolerates partially loadable assemblies: a package may carry dependencies whose own
    // dependencies are absent, while the integration type itself still resolves cleanly.
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    // Per-package isolation context. Shares the SDK contract (and the logging abstractions surfaced
    // through IIntegrationContext) with the default context so boundary types match; everything else —
    // the integration assembly, connectors, and third-party libraries — loads privately from the
    // package directory so different package versions stay isolated from one another.
    private sealed class PackageLoadContext : AssemblyLoadContext
    {
        // Assembly names (not namespaces). The SDK ships as Sdk.dll; the logging abstractions define
        // the ILogger seen on IIntegrationContext. Both must be the agent's instances, not copies.
        private static readonly HashSet<string> SharedContractAssemblies =
            new(StringComparer.OrdinalIgnoreCase) { "Sdk", "Microsoft.Extensions.Logging.Abstractions" };

        private readonly string _directory;

        public PackageLoadContext(string directory)
            : base(name: $"package:{directory}", isCollectible: false)
            => _directory = directory;

        public static bool IsSharedContract(string? assemblyName) =>
            assemblyName is not null && SharedContractAssemblies.Contains(assemblyName);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Defer the shared contract to the default context so boundary types match.
            if (IsSharedContract(assemblyName.Name))
                return null;

            // Load the integration's own dependencies from its package directory. Anything not present
            // there (the shared framework) returns null and resolves from the default context.
            var candidate = Path.Combine(_directory, assemblyName.Name + ".dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }
}
