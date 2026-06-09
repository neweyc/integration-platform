using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Cli.Commands;

/// <summary>
/// Saves an API token for a control plane so later <c>serto deploy</c> calls don't have to re-enter it.
/// The token is verified against the control plane before it is saved, so a typo fails fast instead of
/// at the next deploy.
/// </summary>
public sealed class LoginCommand : AsyncCommand<LoginCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-u|--url")]
        [Description("The Control Plane URL")]
        [DefaultValue("http://localhost:5000")]
        public string ControlPlaneUrl { get; init; } = "http://localhost:5000";

        [CommandOption("-t|--token")]
        [Description("The API token. If omitted, you are prompted (input is hidden).")]
        public string? Token { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        var token = settings.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            token = AnsiConsole.Prompt(new TextPrompt<string>("Enter your [green]API token[/]:").Secret());

        token = token.Trim();
        if (!token.StartsWith("pat_", StringComparison.OrdinalIgnoreCase))
            AnsiConsole.MarkupLine("[yellow]Note:[/] personal access tokens normally start with 'pat_'.");

        var (ok, message) = await VerifyAsync(settings.ControlPlaneUrl, token, ct);
        if (!ok)
        {
            AnsiConsole.MarkupLine($"[red]Login failed:[/] {Markup.Escape(message)}");
            return 1;
        }

        var store = new CredentialStore();
        store.Save(settings.ControlPlaneUrl, token);

        var scope = OperatingSystem.IsWindows() ? string.Empty : " (owner-only)";
        AnsiConsole.MarkupLine(
            $"[green]Logged in to {Markup.Escape(settings.ControlPlaneUrl)}.[/] " +
            $"Token saved to [blue]{Markup.Escape(store.FilePath)}[/]{scope}.");
        return 0;
    }

    // Confirms the token works by making one authenticated request. A valid token returns 200; a rejected
    // token returns 401; anything else (unreachable control plane, etc.) is reported so login fails loudly
    // rather than saving a token that won't work.
    private static async Task<(bool Ok, string Message)> VerifyAsync(string url, string token, CancellationToken ct)
    {
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(url) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync("/api/integrations", ct);
            if (response.IsSuccessStatusCode)
                return (true, "ok");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return (false, "the control plane rejected the token.");

            return (false, $"unexpected response verifying the token ({(int)response.StatusCode} {response.StatusCode}).");
        }
        catch (Exception ex)
        {
            return (false, $"could not reach the control plane at {url} ({ex.Message}).");
        }
    }
}
