using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using IntegrationPlatform.Sdk;
using Shared.Domain;

namespace ControlPlane.Features.IntegrationPackages.Scanning;

public record DiscoveredIntegration(
    string Name,
    string Slug,
    string ClassName,
    TriggerType TriggerType,
    string? CronExpression,
    string? Description,
    int? TimeoutSeconds,
    int? RetryMaxAttempts,
    int? RetryBackoffSeconds);

public interface IAssemblyScanner
{
    List<DiscoveredIntegration> ScanZip(byte[] zipData);
}

public class AssemblyScanner : IAssemblyScanner
{
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

            var dlls = Directory.GetFiles(tempDir, "*.dll");
            var alc = new AssemblyLoadContext("ScanContext", isCollectible: true);

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

    private static List<DiscoveredIntegration> ScanAssembly(Assembly assembly)
    {
        var discovered = new List<DiscoveredIntegration>();
        var types = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IIntegration).IsAssignableFrom(t));

        foreach (var type in types)
        {
            var scheduledAttr = type.GetCustomAttribute<ScheduledIntegrationAttribute>();
            if (scheduledAttr != null)
            {
                discovered.Add(new DiscoveredIntegration(
                    scheduledAttr.Name,
                    scheduledAttr.Slug,
                    type.FullName ?? type.Name,
                    TriggerType.Scheduled,
                    scheduledAttr.CronExpression,
                    scheduledAttr.Description,
                    scheduledAttr.TimeoutSeconds,
                    scheduledAttr.RetryMaxAttempts,
                    scheduledAttr.RetryBackoffSeconds
                ));
                continue;
            }

            var webhookAttr = type.GetCustomAttribute<WebhookIntegrationAttribute>();
            if (webhookAttr != null)
            {
                discovered.Add(new DiscoveredIntegration(
                    webhookAttr.Name,
                    webhookAttr.Slug,
                    type.FullName ?? type.Name,
                    TriggerType.Webhook,
                    null,
                    webhookAttr.Description,
                    webhookAttr.TimeoutSeconds,
                    webhookAttr.RetryMaxAttempts,
                    webhookAttr.RetryBackoffSeconds
                ));
                continue;
            }
        }

        return discovered;
    }
}
