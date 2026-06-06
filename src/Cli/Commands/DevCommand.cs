using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Cli.Commands;

public sealed class DevCommand : AsyncCommand<DevCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[ClassName]")]
        [Description("The fully qualified name of the integration class to test")]
        public string? ClassName { get; init; }

        [CommandOption("-s|--secrets")]
        [Description("Path to a JSON file containing secrets")]
        public string? SecretsPath { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new FigletText("serto dev").Color(Color.Blue));
        AnsiConsole.MarkupLine("[yellow]Watching for file changes...[/] Press [red]Ctrl+C[/] to stop.");
        AnsiConsole.MarkupLine("------------------------------------------------------------");

        var currentDir = Directory.GetCurrentDirectory();
        
        // Initial run
        await RunTestAsync(settings, ct);

        using var watcher = new FileSystemWatcher(currentDir);
        watcher.IncludeSubdirectories = true;
        watcher.Filter = "*.cs";
        watcher.EnableRaisingEvents = true;

        var lastEventTime = DateTime.MinValue;
        var debounceTime = TimeSpan.FromMilliseconds(500);

        watcher.Changed += async (s, e) =>
        {
            if (DateTime.Now - lastEventTime < debounceTime) return;
            lastEventTime = DateTime.Now;

            AnsiConsole.MarkupLine($"\n[blue][[{DateTime.Now:HH:mm:ss}]][/] Change detected: [green]{e.Name}[/]");
            await RunTestAsync(settings, ct);
        };

        try
        {
            await Task.Delay(-1, ct);
        }
        catch (TaskCanceledException)
        {
            // Exit gracefully
        }

        return 0;
    }

    private async Task RunTestAsync(Settings settings, CancellationToken ct)
    {
        try
        {
            var testCommand = new TestCommand();
            var testSettings = new TestCommand.Settings
            {
                ClassName = settings.ClassName,
                SecretsPath = settings.SecretsPath
            };

            await testCommand.RunAsync(testSettings, ct);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error during test execution:[/] {ex.Message}");
        }
    }
}
