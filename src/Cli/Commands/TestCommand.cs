using System.ComponentModel;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Cronos;
using Serto.Sdk;
using Serto.Testing;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Cli.Commands;

public sealed class TestCommand : AsyncCommand<TestCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[ClassName]")]
        [Description("The fully qualified name of the integration class to test")]
        public string? ClassName { get; init; }

        [CommandOption("-s|--secrets")]
        [Description("Path to a JSON file containing secrets")]
        public string? SecretsPath { get; init; }

        [CommandOption("-p|--payload")]
        [Description("Raw payload for webhook testing")]
        public string? Payload { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        return await RunAsync(settings, ct);
    }

    public async Task<int> RunAsync(Settings settings, CancellationToken ct)
    {
        var currentDir = Directory.GetCurrentDirectory();

        // 1. Build project — abort on failure so we never run a stale DLL or fail later with a
        // confusing "assembly not found".
        var buildSucceeded = true;
        var buildOutput = string.Empty;
        await AnsiConsole.Status()
            .StartAsync("Building project for testing...", async ctx =>
            {
                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "build",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null) throw new Exception("Failed to start dotnet build");

                // Read both streams before waiting so a large build log can't fill a pipe and deadlock.
                var stdout = await process.StandardOutput.ReadToEndAsync(ct);
                var stderr = await process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);

                buildSucceeded = process.ExitCode == 0;
                buildOutput = string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stderr}{Environment.NewLine}{stdout}";
            });

        if (!buildSucceeded)
        {
            AnsiConsole.MarkupLine("[red]✗ Build failed.[/] Fix the compile errors below before testing.");
            AnsiConsole.WriteLine(buildOutput.Trim());
            return 1;
        }

        // 2. Find DLL
        var binDir = Path.Combine(currentDir, "bin", "Debug", "net10.0");
        var csprojFile = Directory.GetFiles(currentDir, "*.csproj").FirstOrDefault();
        if (csprojFile == null) throw new Exception("No .csproj found");
        
        var assemblyPath = Path.Combine(binDir, Path.GetFileNameWithoutExtension(csprojFile) + ".dll");
        if (!File.Exists(assemblyPath)) throw new Exception($"Assembly not found at {assemblyPath}");

        // 3. Load and find class
        var alc = new AssemblyLoadContext("TestContext", isCollectible: true);
        var assembly = alc.LoadFromAssemblyPath(assemblyPath);
        
        var type = settings.ClassName != null 
            ? assembly.GetType(settings.ClassName)
            : assembly.GetTypes().FirstOrDefault(t => typeof(IIntegration).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        if (type == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Integration class not found.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[blue]Testing integration:[/] [green]{type.FullName}[/]");

        var secrets = await LoadSecretsAsync(settings.SecretsPath, ct);

        // 4. Preflight: the same structural checks the control plane runs before accepting an integration,
        // plus a required-secret cross-check, so common "works locally, breaks on deploy" mistakes surface
        // now instead of at deploy or first run.
        var requiredSecrets = ScanCommand.DiscoverRequiredSecrets(currentDir);
        var preflight = Preflight(type, requiredSecrets, secrets.Keys);

        foreach (var warning in preflight.Warnings)
            AnsiConsole.MarkupLine($"[yellow]⚠ {Markup.Escape(warning)}[/]");
        foreach (var error in preflight.Errors)
            AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(error)}[/]");

        if (preflight.HasErrors)
        {
            AnsiConsole.MarkupLine("[red]Validation failed.[/] Fix the issues above before deploy.");
            return 1;
        }

        // 5. Run
        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("Test");
        var testContext = new TestIntegrationContext
        {
            Logger = logger,
            Secrets = secrets,
            Payload = settings.Payload
        };

        IIntegration instance;
        try
        {
            instance = (IIntegration)Activator.CreateInstance(type)!;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ Could not create {Markup.Escape(type.Name)}:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        AnsiConsole.MarkupLine("[yellow]------------------- EXECUTION START -------------------[/]");
        await instance.RunAsync(testContext, ct);
        AnsiConsole.MarkupLine("[yellow]-------------------- EXECUTION END --------------------[/]");

        AnsiConsole.MarkupLine("[green]Local test run completed.[/]");

        return 0;
    }

    /// <summary>
    /// Structural validation of an integration class before it runs, mirroring what the control plane
    /// enforces at deploy: a discovery attribute must be present, a scheduled integration's cron must be
    /// valid, and the class must be instantiable with no arguments. Returns hard errors (which should stop
    /// the run) and warnings (missing secrets, which should not). Pure and reflection-based so it is unit
    /// testable without building a project.
    /// </summary>
    public static TestPreflightResult Preflight(
        Type type, IReadOnlyList<string> requiredSecrets, IEnumerable<string> providedSecretKeys)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var attribute = type.GetCustomAttribute<IntegrationAttribute>(inherit: false);
        if (attribute is null)
        {
            errors.Add($"{type.Name} has no [Integration]/[ScheduledIntegration]/[WebhookIntegration] attribute, so the control plane will not discover it on deploy.");
        }
        else if (attribute is ScheduledIntegrationAttribute scheduled)
        {
            if (string.IsNullOrWhiteSpace(scheduled.CronExpression))
                errors.Add("The scheduled integration is missing a cron expression.");
            else if (!IsValidCron(scheduled.CronExpression, out var reason))
                errors.Add($"Cron \"{scheduled.CronExpression}\" is not a valid cron expression{reason}.");
        }

        if (type.GetConstructor(Type.EmptyTypes) is null)
            errors.Add($"{type.Name} needs a public parameterless constructor; the runtime agent creates it with no arguments.");

        var provided = new HashSet<string>(providedSecretKeys, StringComparer.OrdinalIgnoreCase);
        foreach (var secret in requiredSecrets)
        {
            if (!provided.Contains(secret))
                warnings.Add($"{secret} is referenced in code but missing from --secrets; the integration may fail when it needs it.");
        }

        return new TestPreflightResult(errors, warnings);
    }

    private static bool IsValidCron(string expression, out string reason)
    {
        try
        {
            CronExpression.Parse(expression);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = $" ({ex.Message})";
            return false;
        }
    }

    public static async Task<IReadOnlyDictionary<string, string>> LoadSecretsAsync(string? secretsPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(secretsPath))
        {
            return new Dictionary<string, string>();
        }

        if (!File.Exists(secretsPath))
        {
            throw new FileNotFoundException("Secrets file not found.", secretsPath);
        }

        await using var stream = File.OpenRead(secretsPath);
        var secrets = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: ct);
        return secrets ?? new Dictionary<string, string>();
    }
}

public sealed record TestPreflightResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool HasErrors => Errors.Count > 0;
}
