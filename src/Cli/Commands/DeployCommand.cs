using System.ComponentModel;
using System.IO.Compression;
using System.Net.Http.Headers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Cli.Commands;

public sealed class DeployCommand : AsyncCommand<DeployCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-e|--environment")]
        [Description("The environment to deploy to")]
        [DefaultValue("production")]
        public string Environment { get; init; } = "production";

        [CommandOption("-u|--url")]
        [Description("The Control Plane URL")]
        [DefaultValue("http://localhost:5000")]
        public string ControlPlaneUrl { get; init; } = "http://localhost:5000";

        [CommandOption("-t|--token")]
        [Description("The API token")]
        public string? Token { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        var currentDir = Directory.GetCurrentDirectory();
        var csprojFile = Directory.GetFiles(currentDir, "*.csproj").FirstOrDefault();

        if (csprojFile == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] No .csproj file found in the current directory.");
            return 1;
        }

        var projectName = Path.GetFileNameWithoutExtension(csprojFile);
        AnsiConsole.MarkupLine($"[blue]Deploying integration project:[/] [green]{projectName}[/]");

        // 1. Build
        await AnsiConsole.Status()
            .StartAsync("Building project...", async ctx =>
            {
                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "publish -c Release -o ./publish",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null) throw new Exception("Failed to start dotnet publish");
                await process.WaitForExitAsync(ct);

                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync(ct);
                    throw new Exception($"Build failed: {error}");
                }
            });

        // 2. Package
        var zipPath = Path.Combine(currentDir, "bundle.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);

        await AnsiConsole.Status()
            .StartAsync("Packaging bundle...", async ctx =>
            {
                ZipFile.CreateFromDirectory("./publish", zipPath);
            });

        // 3. Upload
        var token = settings.Token ?? AnsiConsole.Ask<string>("Enter your [green]API token[/]:");

        await AnsiConsole.Status()
            .StartAsync("Uploading to Control Plane...", async ctx =>
            {
                using var client = new HttpClient();
                client.BaseAddress = new Uri(settings.ControlPlaneUrl);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var content = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(zipPath, ct));
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");

                content.Add(new StringContent(projectName), "name");
                content.Add(new StringContent("1.0." + DateTime.UtcNow.ToString("yyyyMMddHHmm")), "version");
                content.Add(fileContent, "file", "bundle.zip");

                var response = await client.PostAsync("/api/integration-packages", content, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(ct);
                    throw new Exception($"Upload failed: {response.StatusCode} - {error}");
                }
            });

        AnsiConsole.MarkupLine("[green]Success![/] Project deployed and integrations auto-provisioned.");
        
        // Cleanup
        File.Delete(zipPath);
        Directory.Delete("./publish", true);

        return 0;
    }
}
