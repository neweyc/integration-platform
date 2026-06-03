using System.ComponentModel;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using IntegrationPlatform.Sdk;
using IntegrationPlatform.Testing;
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
        
        // 1. Build project
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
                await process.WaitForExitAsync(ct);
            });

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

        // 4. Run
        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("Test");
        var testContext = new TestIntegrationContext
        {
            Logger = logger,
            Secrets = await LoadSecretsAsync(settings.SecretsPath, ct),
            Payload = settings.Payload
        };

        var instance = (IIntegration)Activator.CreateInstance(type)!;
        
        AnsiConsole.MarkupLine("[yellow]------------------- EXECUTION START -------------------[/]");
        await instance.RunAsync(testContext, ct);
        AnsiConsole.MarkupLine("[yellow]-------------------- EXECUTION END --------------------[/]");

        AnsiConsole.MarkupLine("[green]Local test run completed.[/]");

        return 0;
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
