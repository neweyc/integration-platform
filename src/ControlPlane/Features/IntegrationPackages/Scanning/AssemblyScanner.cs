using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using Serto.Sdk;
using Shared.Domain;

namespace ControlPlane.Features.IntegrationPackages.Scanning;

public record DiscoveredIntegration(
    string Name,
    string Slug,
    string ClassName,
    string? Description,
    int? TimeoutSeconds,
    int? RetryMaxAttempts,
    int? RetryBackoffSeconds,
    IReadOnlyList<DiscoveredIntegrationTrigger> Triggers,
    IReadOnlyList<string>? RequiredTags = null);

public record DiscoveredIntegrationTrigger(
    string Name,
    string Slug,
    TriggerType Type,
    string? CronExpression);

public interface IAssemblyScanner
{
    List<DiscoveredIntegration> ScanZip(byte[] zipData);
}

public class AssemblyScanner : IAssemblyScanner
{
    private const string IntegrationInterfaceName = "Serto.Sdk.IIntegration";
    private const string IntegrationAttributeName = "Serto.Sdk.IntegrationAttribute";
    private const string ScheduledAttributeName = "Serto.Sdk.ScheduledIntegrationAttribute";
    private const string WebhookAttributeName = "Serto.Sdk.WebhookIntegrationAttribute";
    private const string RequiresCapabilitiesAttributeName = "Serto.Sdk.RequiresAgentCapabilitiesAttribute";

    public List<DiscoveredIntegration> ScanZip(byte[] zipData)
    {
        var discovered = new List<DiscoveredIntegration>();
        var tempDir = Path.Combine(Path.GetTempPath(), "ip-scan-" + Guid.NewGuid());
        
        try
        {
            Directory.CreateDirectory(tempDir);
            using (var stream = new MemoryStream(zipData))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(tempDir);
            }

            var dlls = Directory.GetFiles(tempDir, "*.dll", SearchOption.AllDirectories);
            var dllByName = dlls
                .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key ?? string.Empty, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var alc = new AssemblyLoadContext("ScanContext", isCollectible: true);
            alc.Resolving += (_, assemblyName) =>
                assemblyName.Name is not null && dllByName.TryGetValue(assemblyName.Name, out var dependencyPath)
                    ? alc.LoadFromAssemblyPath(dependencyPath)
                    : null;

            try
            {
                foreach (var dllPath in dlls)
                {
                    try
                    {
                        var assembly = alc.LoadFromAssemblyPath(dllPath);
                        discovered.AddRange(ScanAssembly(assembly));
                    }
                    catch
                    {
                        // Skip DLLs that can't be loaded (e.g., native dependencies)
                    }
                }
            }
            finally
            {
                alc.Unload();
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }

        return discovered;
    }

    public static List<DiscoveredIntegration> ScanAssembly(Assembly assembly)
    {
        var discovered = new List<DiscoveredIntegration>();
        var types = GetLoadableTypes(assembly)
            .Where(t => t.IsClass
                     && !t.IsAbstract
                     && t.GetInterfaces().Any(i => i.FullName == IntegrationInterfaceName));

        foreach (var type in types)
        {
            var attributes = type.GetCustomAttributes(inherit: false).ToList();
            var integrationAttr = attributes.FirstOrDefault(a => a.GetType().FullName == IntegrationAttributeName);
            var triggerMetadataAttr = attributes.FirstOrDefault(a =>
                a.GetType().FullName is ScheduledAttributeName or WebhookAttributeName);
            var metadataAttr = integrationAttr ?? triggerMetadataAttr;

            if (metadataAttr is null)
                continue;

            var requiredTags = attributes
                .Where(a => a.GetType().FullName == RequiresCapabilitiesAttributeName)
                .SelectMany(a => GetStringArray(a, "Tags"))
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var triggers = new List<DiscoveredIntegrationTrigger>();
            foreach (var attribute in attributes)
            {
                switch (attribute.GetType().FullName)
                {
                    case ScheduledAttributeName:
                        triggers.Add(new DiscoveredIntegrationTrigger(
                            "Scheduled",
                            "scheduled",
                            TriggerType.Scheduled,
                            GetString(attribute, "CronExpression")));
                        break;
                    case WebhookAttributeName:
                        triggers.Add(new DiscoveredIntegrationTrigger(
                            "Webhook",
                            "webhook",
                            TriggerType.Webhook,
                            CronExpression: null));
                        break;
                }
            }

            discovered.Add(new DiscoveredIntegration(
                GetRequiredString(metadataAttr, "Name"),
                GetRequiredString(metadataAttr, "Slug"),
                type.FullName ?? type.Name,
                GetString(metadataAttr, "Description"),
                GetNullableInt(metadataAttr, "TimeoutSeconds"),
                GetNullableInt(metadataAttr, "RetryMaxAttempts"),
                GetNullableInt(metadataAttr, "RetryBackoffSeconds"),
                triggers,
                requiredTags));
        }

        return discovered;
    }

    private static IReadOnlyList<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Cast<Type>().ToList();
        }
    }

    private static string GetRequiredString(object attribute, string propertyName) =>
        GetString(attribute, propertyName) ?? string.Empty;

    private static string? GetString(object attribute, string propertyName) =>
        attribute.GetType().GetProperty(propertyName)?.GetValue(attribute) as string;

    private static string[] GetStringArray(object attribute, string propertyName) =>
        attribute.GetType().GetProperty(propertyName)?.GetValue(attribute) as string[] ?? [];

    private static int? GetNullableInt(object attribute, string propertyName) =>
        attribute.GetType().GetProperty(propertyName)?.GetValue(attribute) switch
        {
            int value when value > 0 => value,
            _ => null
        };
}
