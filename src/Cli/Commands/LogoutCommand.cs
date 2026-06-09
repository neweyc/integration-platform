using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Cli.Commands;

/// <summary>
/// Removes a saved API token. By default it clears the token for the given control plane; <c>--all</c>
/// clears every saved credential.
/// </summary>
public sealed class LogoutCommand : AsyncCommand<LogoutCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-u|--url")]
        [Description("The Control Plane URL to clear. Defaults to your last `serto login`.")]
        public string? ControlPlaneUrl { get; init; }

        [CommandOption("--all")]
        [Description("Clear all saved credentials")]
        public bool All { get; init; }
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        var store = new CredentialStore();

        if (settings.All)
        {
            store.Clear();
            AnsiConsole.MarkupLine("[green]Cleared all saved credentials.[/]");
            return Task.FromResult(0);
        }

        var url = settings.ControlPlaneUrl ?? store.GetDefaultUrl() ?? "http://localhost:5000";

        if (store.Remove(url))
            AnsiConsole.MarkupLine($"[green]Logged out of {Markup.Escape(url)}.[/]");
        else
            AnsiConsole.MarkupLine($"[yellow]No saved credentials for {Markup.Escape(url)}.[/]");

        return Task.FromResult(0);
    }
}
