using System.ComponentModel;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Xml.Linq;
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

        [CommandOption("-v|--version")]
        [Description("The package version to deploy. Defaults to PackageVersion/Version from the project file.")]
        public string? Version { get; init; }

        [CommandOption("-n|--name")]
        [Description("The package name to upload. Defaults to the project name.")]
        public string? PackageName { get; init; }
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
        var packageName = string.IsNullOrWhiteSpace(settings.PackageName) ? projectName : settings.PackageName.Trim();
        var packageVersion = ResolvePackageVersion(csprojFile, settings.Version, DateTimeOffset.UtcNow);

        AnsiConsole.MarkupLine($"[blue]Deploying integration project:[/] [green]{projectName}[/]");
        AnsiConsole.MarkupLine($"[blue]Package:[/] [green]{packageName}[/] [blue]version:[/] [green]{packageVersion}[/]");

        var publishDir = Path.Combine(currentDir, "publish");
        var zipPath = Path.Combine(currentDir, CreatePackageArchiveFileName(packageName, packageVersion));

        try
        {
            // 1. Build
            await AnsiConsole.Status()
                .StartAsync("Building project...", async ctx =>
                {
                    var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"publish \"{csprojFile}\" -c Release -o \"{publishDir}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });

                    if (process == null) throw new Exception("Failed to start dotnet publish");

                    var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
                    var stderrTask = process.StandardError.ReadToEndAsync(ct);
                    await process.WaitForExitAsync(ct);

                    var stdout = await stdoutTask;
                    var stderr = await stderrTask;

                    if (process.ExitCode != 0)
                    {
                        throw new Exception($"Build failed: {stderr}{Environment.NewLine}{stdout}");
                    }
                });

            // 2. Package
            if (File.Exists(zipPath)) File.Delete(zipPath);

            await AnsiConsole.Status()
                .StartAsync("Packaging bundle...", async ctx =>
                {
                    await Task.Run(() => ZipFile.CreateFromDirectory(publishDir, zipPath), ct);
                });

            // 3. Upload
            var token = ResolveToken(settings.Token, Environment.GetEnvironmentVariable("IP_API_TOKEN"))
                        ?? AnsiConsole.Ask<string>("Enter your [green]API token[/]:");

            await AnsiConsole.Status()
                .StartAsync("Uploading to Control Plane...", async ctx =>
                {
                    using var client = new HttpClient();
                    client.BaseAddress = new Uri(settings.ControlPlaneUrl);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    using var content = new MultipartFormDataContent();
                    var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(zipPath, ct));
                    fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");

                    content.Add(new StringContent(packageName), "name");
                    content.Add(new StringContent(packageVersion), "version");
                    content.Add(fileContent, "file", Path.GetFileName(zipPath));

                    var response = await client.PostAsync("/api/integration-packages", content, ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync(ct);
                        throw new Exception($"Upload failed: {response.StatusCode} - {error}");
                    }
                });

            AnsiConsole.MarkupLine("[green]Success![/] Project deployed and integrations auto-provisioned.");
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            if (Directory.Exists(publishDir)) Directory.Delete(publishDir, true);
        }

        return 0;
    }

    public static string? ResolveToken(string? explicitToken, string? environmentToken)
    {
        if (!string.IsNullOrWhiteSpace(explicitToken)) return explicitToken.Trim();
        if (!string.IsNullOrWhiteSpace(environmentToken)) return environmentToken.Trim();
        return null;
    }

    public static string ResolvePackageVersion(string csprojPath, string? explicitVersion, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(explicitVersion)) return explicitVersion.Trim();

        var document = XDocument.Load(csprojPath);
        var packageVersion = document.Descendants("PackageVersion").FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(packageVersion)) return packageVersion.Trim();

        var version = document.Descendants("Version").FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(version)) return version.Trim();

        return "0.1.0-dev." + now.UtcDateTime.ToString("yyyyMMddHHmmss");
    }

    public static string CreatePackageArchiveFileName(string packageName, string packageVersion)
    {
        var invalidChars = Path.GetInvalidFileNameChars()
            .Concat(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\' })
            .ToHashSet();
        var safeName = new string(packageName.Select(c => invalidChars.Contains(c) ? '-' : c).ToArray());
        var safeVersion = new string(packageVersion.Select(c => invalidChars.Contains(c) ? '-' : c).ToArray());

        return $"{safeName}.{safeVersion}.zip";
    }
}
