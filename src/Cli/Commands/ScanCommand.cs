using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Cronos;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Cli.Commands;

public sealed class ScanCommand : AsyncCommand<ScanCommand.Settings>
{
    private const string IntegrationInterfaceName = "Serto.Sdk.IIntegration";
    private const string IntegrationAttributeName = "Serto.Sdk.IntegrationAttribute";
    private const string ScheduledAttributeName = "Serto.Sdk.ScheduledIntegrationAttribute";
    private const string WebhookAttributeName = "Serto.Sdk.WebhookIntegrationAttribute";

    public sealed class Settings : CommandSettings
    {
        [CommandOption("-p|--project")]
        [Description("Path to the integration .csproj. Defaults to the first .csproj in the current directory.")]
        public string? ProjectPath { get; init; }

        [CommandOption("-v|--version")]
        [Description("Package version to preview. Defaults to PackageVersion/Version from the project file.")]
        public string? Version { get; init; }

        [CommandOption("-n|--name")]
        [Description("Package name to preview. Defaults to the project name.")]
        public string? PackageName { get; init; }

        [CommandOption("-c|--configuration")]
        [Description("Build configuration to scan.")]
        [DefaultValue("Debug")]
        public string Configuration { get; init; } = "Debug";

        [CommandOption("--no-build")]
        [Description("Skip building and scan the existing bin output.")]
        public bool NoBuild { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        var csprojFile = ResolveProjectPath(settings.ProjectPath, Directory.GetCurrentDirectory());
        if (csprojFile is null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] No .csproj file found.");
            return 1;
        }

        var projectName = Path.GetFileNameWithoutExtension(csprojFile);
        var packageName = string.IsNullOrWhiteSpace(settings.PackageName) ? projectName : settings.PackageName.Trim();
        var packageVersion = DeployCommand.ResolvePackageVersion(csprojFile, settings.Version, DateTimeOffset.UtcNow);
        var targetFramework = ResolveTargetFramework(csprojFile);
        var outputDir = ResolveBuildOutputDirectory(csprojFile, settings.Configuration, targetFramework);

        if (!settings.NoBuild)
            await AnsiConsole.Status()
                .StartAsync("Building project for scan...", async _ =>
                    await BuildAsync(csprojFile, settings.Configuration, ct));

        if (!Directory.Exists(outputDir))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Build output not found at '{Markup.Escape(outputDir)}'. Run without --no-build or build the project first.");
            return 1;
        }

        var result = ScanDirectory(outputDir)
            .WithRequiredSecrets(DiscoverRequiredSecrets(Path.GetDirectoryName(csprojFile)!));

        RenderPreview(projectName, packageName, packageVersion, result);
        return result.IsValid ? 0 : 1;
    }

    public static string? ResolveProjectPath(string? explicitPath, string currentDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath);

        return Directory.GetFiles(currentDirectory, "*.csproj").FirstOrDefault();
    }

    public static ScanResult ScanDirectory(string directory)
    {
        var dlls = Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories);
        var dllByName = dlls
            .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key ?? string.Empty, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var (discovered, warnings) = LoadAssemblies(dlls, dllByName);

        // Reclaim the collectible ALC and the in-memory assembly images it loaded. This is no longer
        // load-bearing for file locks — assemblies are loaded from byte arrays (see LoadIntoContext),
        // so no file in the scanned directory is ever mapped or locked, and the caller can delete the
        // publish directory safely regardless of GC timing. It just frees memory promptly.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        if (discovered.Count == 0)
            warnings.Add("No decorated IIntegration classes were discovered.");

        var errors = new List<string>();
        foreach (var integration in discovered)
            errors.AddRange(Validate(integration));

        return new ScanResult(discovered, warnings, errors);
    }

    // NoInlining ensures the ALC and all assembly references are fully out of scope
    // before the caller forces GC, so the collectible context can actually be collected.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (List<ScannedIntegration> Discovered, List<string> Warnings) LoadAssemblies(
        string[] dlls,
        Dictionary<string, string> dllByName)
    {
        var discovered = new List<ScannedIntegration>();
        var warnings = new List<string>();
        var alc = new AssemblyLoadContext("CliScanContext", isCollectible: true);

        // Loaded assemblies keyed by simple name. Unlike LoadFromAssemblyPath, LoadFromStream is not
        // idempotent — loading the same identity twice throws — so we cache and reuse. Both the scan
        // loop and the dependency resolver go through this, so an assembly is only ever loaded once.
        var loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        // Loads an assembly from its bytes rather than its path. LoadFromAssemblyPath memory-maps the
        // file and holds an OS lock on it until the ALC is collected, which made deletes of the publish
        // directory fail with "access to path '<assembly>.dll' is denied" whenever an assembly slow to
        // collect (e.g. Azure.Core, pulled in transitively by a connector) had been mapped. Reading the
        // bytes first copies the image into managed memory and closes the file immediately, so no file
        // in the scanned directory is ever locked.
        Assembly LoadIntoContext(string path)
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            var assembly = alc.LoadFromStream(stream);
            var name = assembly.GetName().Name;
            if (name is not null)
                loaded[name] = assembly;
            return assembly;
        }

        alc.Resolving += (_, assemblyName) =>
        {
            if (assemblyName.Name is null)
                return null;
            if (loaded.TryGetValue(assemblyName.Name, out var cached))
                return cached;
            return dllByName.TryGetValue(assemblyName.Name, out var dependencyPath)
                ? LoadIntoContext(dependencyPath)
                : null;
        };

        try
        {
            foreach (var dllPath in dlls)
            {
                try
                {
                    // A dependency resolved while scanning an earlier assembly may already be loaded;
                    // reuse it rather than re-loading from stream (which would throw on the duplicate).
                    var name = Path.GetFileNameWithoutExtension(dllPath);
                    var assembly = loaded.TryGetValue(name, out var existing) ? existing : LoadIntoContext(dllPath);
                    discovered.AddRange(ScanAssembly(assembly));
                }
                catch (BadImageFormatException)
                {
                    warnings.Add($"Skipped non-.NET assembly '{Path.GetFileName(dllPath)}'.");
                }
                catch (FileLoadException ex)
                {
                    warnings.Add($"Skipped '{Path.GetFileName(dllPath)}': {ex.Message}");
                }
            }
        }
        finally
        {
            alc.Unload();
        }

        return (discovered, warnings);
    }

    public static IReadOnlyList<ScannedIntegration> ScanAssembly(Assembly assembly)
    {
        var discovered = new List<ScannedIntegration>();
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

            var triggers = new List<ScannedTrigger>();
            foreach (var attribute in attributes)
            {
                switch (attribute.GetType().FullName)
                {
                    case ScheduledAttributeName:
                        triggers.Add(new ScannedTrigger("Scheduled", "scheduled", "Scheduled", GetString(attribute, "CronExpression")));
                        break;
                    case WebhookAttributeName:
                        triggers.Add(new ScannedTrigger("Webhook", "webhook", "Webhook", CronExpression: null));
                        break;
                }
            }

            discovered.Add(new ScannedIntegration(
                GetRequiredString(metadataAttr, "Name"),
                GetRequiredString(metadataAttr, "Slug"),
                type.FullName ?? type.Name,
                GetString(metadataAttr, "Description"),
                GetPositiveInt(metadataAttr, "TimeoutSeconds"),
                GetPositiveInt(metadataAttr, "RetryMaxAttempts"),
                GetPositiveInt(metadataAttr, "RetryBackoffSeconds"),
                triggers));
        }

        return discovered;
    }

    public static string ResolveTargetFramework(string csprojFile)
    {
        var document = System.Xml.Linq.XDocument.Load(csprojFile);
        var targetFramework = document.Descendants("TargetFramework").FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(targetFramework))
            return targetFramework.Trim();

        var targetFrameworks = document.Descendants("TargetFrameworks").FirstOrDefault()?.Value;
        var first = targetFrameworks?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(first))
            return first;

        throw new InvalidOperationException("Project file must define TargetFramework or TargetFrameworks.");
    }

    public static string ResolveBuildOutputDirectory(string csprojFile, string configuration, string targetFramework) =>
        Path.Combine(Path.GetDirectoryName(csprojFile)!, "bin", configuration, targetFramework);

    private static async Task BuildAsync(string csprojFile, string configuration, CancellationToken ct)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{csprojFile}\" -c \"{configuration}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process is null)
            throw new InvalidOperationException("Failed to start dotnet build.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Build failed: {stderr}{Environment.NewLine}{stdout}");
    }

    public static void RenderPreview(string projectName, string packageName, string packageVersion, ScanResult result)
    {
        AnsiConsole.MarkupLine($"[blue]Project:[/] [green]{Markup.Escape(projectName)}[/]");
        AnsiConsole.MarkupLine($"[blue]Package:[/] [green]{Markup.Escape(packageName)}[/] [blue]version:[/] [green]{Markup.Escape(packageVersion)}[/]");

        var table = new Table().Title("Discovered integrations");
        table.AddColumn("Name");
        table.AddColumn("Slug");
        table.AddColumn("Class");
        table.AddColumn("Triggers");
        table.AddColumn("Run policy");

        foreach (var integration in result.Integrations)
        {
            var triggers = integration.Triggers.Count == 0
                ? "none"
                : string.Join(", ", integration.Triggers.Select(t =>
                    t.Type == "Scheduled" ? $"{t.Slug} ({t.CronExpression})" : t.Slug));
            var policy = $"timeout: {integration.TimeoutSeconds?.ToString() ?? "default"}, retries: {integration.RetryMaxAttempts?.ToString() ?? "0"}";
            table.AddRow(
                Markup.Escape(integration.Name),
                Markup.Escape(integration.Slug),
                Markup.Escape(integration.ClassName),
                Markup.Escape(triggers),
                Markup.Escape(policy));
        }

        if (result.Integrations.Count > 0)
            AnsiConsole.Write(table);

        if (result.RequiredSecrets.Count > 0)
        {
            AnsiConsole.MarkupLine("[blue]Required secrets:[/] " +
                                   string.Join(", ", result.RequiredSecrets.Select(s => $"[green]{Markup.Escape(s)}[/]")));
        }
        else
        {
            AnsiConsole.MarkupLine("[blue]Required secrets:[/] none detected");
        }

        foreach (var warning in result.Warnings)
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");

        foreach (var error in result.Errors)
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(error)}");

        AnsiConsole.MarkupLine(result.IsValid
            ? "[green]Scan passed.[/] Package is ready for deploy preview."
            : "[red]Scan failed.[/] Fix validation errors before deploy.");
    }

    private static IEnumerable<string> Validate(ScannedIntegration integration)
    {
        if (string.IsNullOrWhiteSpace(integration.Name))
            yield return $"{integration.ClassName}: integration name is required.";

        if (!IsSlug(integration.Slug))
            yield return $"{integration.ClassName}: integration slug '{integration.Slug}' must contain only lowercase letters, numbers, and hyphens.";

        if (!Regex.IsMatch(integration.ClassName, @"^[\w]+(?:\.[\w]+)*$"))
            yield return $"{integration.ClassName}: class name must be a valid fully-qualified .NET type name.";

        // Note: timeout/retry are read via GetPositiveInt, which coerces zero/negative attribute
        // values to null (matching the server scanner). Non-positive values are silently treated
        // as "unset", so there is no separate numeric guard here — that keeps the preview faithful
        // to what deploy will actually provision.

        var triggerSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var trigger in integration.Triggers)
        {
            if (string.IsNullOrWhiteSpace(trigger.Name))
                yield return $"{integration.ClassName}: trigger name is required.";

            if (!IsSlug(trigger.Slug))
                yield return $"{integration.ClassName}: trigger slug '{trigger.Slug}' must contain only lowercase letters, numbers, and hyphens.";

            if (!triggerSlugs.Add(trigger.Slug))
                yield return $"{integration.ClassName}: duplicate trigger slug '{trigger.Slug}'.";

            if (trigger.Type == "Scheduled")
            {
                if (string.IsNullOrWhiteSpace(trigger.CronExpression))
                    yield return $"{integration.ClassName}: scheduled trigger '{trigger.Slug}' requires a cron expression.";
                else if (!IsValidCronExpression(trigger.CronExpression))
                    yield return $"{integration.ClassName}: '{trigger.CronExpression}' is not a valid cron expression.";
            }
        }
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

    private static int? GetPositiveInt(object attribute, string propertyName) =>
        attribute.GetType().GetProperty(propertyName)?.GetValue(attribute) switch
        {
            int value when value > 0 => value,
            _ => null
        };

    private static bool IsSlug(string slug) =>
        Regex.IsMatch(slug, "^[a-z0-9-]+$");

    private static bool IsValidCronExpression(string expression)
    {
        try
        {
            CronExpression.Parse(expression);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<string> DiscoverRequiredSecrets(string projectDirectory)
    {
        if (!Directory.Exists(projectDirectory))
            return [];

        var secrets = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var patterns = new[]
        {
            @"\.Secrets\s*\[\s*""(?<secret>[A-Za-z0-9_.:-]+)""\s*\]",
            @"\.Secrets\s*\.TryGetValue\s*\(\s*""(?<secret>[A-Za-z0-9_.:-]+)""",
            @"\.WithBearerToken\s*\(\s*""(?<secret>[A-Za-z0-9_.:-]+)""\s*\)",
            @"\.WithApiKeyHeader\s*\(\s*""[^""]+""\s*,\s*""(?<secret>[A-Za-z0-9_.:-]+)""\s*\)",
            @"\.WithApiKeyQuery\s*\(\s*""[^""]+""\s*,\s*""(?<secret>[A-Za-z0-9_.:-]+)""\s*\)",
            @"\.WithBasicAuth\s*\(\s*""(?<secret>[A-Za-z0-9_.:-]+)""\s*,\s*""(?<secret>[A-Za-z0-9_.:-]+)""\s*\)",
            @"\.SqlConnector\s*\(\s*""(?<secret>[A-Za-z0-9_.:-]+)""\s*\)"
        };

        foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var source = File.ReadAllText(file);
            foreach (var pattern in patterns)
            {
                foreach (Match match in Regex.Matches(source, pattern))
                {
                    foreach (Capture capture in match.Groups["secret"].Captures)
                        secrets.Add(capture.Value);
                }
            }
        }

        return secrets.ToList();
    }
}

public record ScanResult(
    IReadOnlyList<ScannedIntegration> Integrations,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> RequiredSecrets)
{
    public ScanResult(
        IReadOnlyList<ScannedIntegration> integrations,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors)
        : this(integrations, warnings, errors, [])
    {
    }

    public bool IsValid => Errors.Count == 0;

    public ScanResult WithRequiredSecrets(IReadOnlyList<string> requiredSecrets) =>
        this with { RequiredSecrets = requiredSecrets };
}

public record ScannedIntegration(
    string Name,
    string Slug,
    string ClassName,
    string? Description,
    int? TimeoutSeconds,
    int? RetryMaxAttempts,
    int? RetryBackoffSeconds,
    IReadOnlyList<ScannedTrigger> Triggers);

public record ScannedTrigger(
    string Name,
    string Slug,
    string Type,
    string? CronExpression);
