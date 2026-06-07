using System.ComponentModel;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

        [CommandOption("-p|--project")]
        [Description("Path to the integration .csproj. Defaults to the first .csproj in the current directory.")]
        public string? ProjectPath { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        var csprojFile = ScanCommand.ResolveProjectPath(settings.ProjectPath, Directory.GetCurrentDirectory());

        if (csprojFile is null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] No .csproj file found.");
            return 1;
        }

        PackageBuildResult? package = null;

        try
        {
            AnsiConsole.MarkupLine("[blue]Preparing deploy preview...[/]");
            package = await PackageCommand.CreateAsync(
                csprojFile,
                settings.PackageName,
                settings.Version,
                outputDirectory: null,
                keepArchive: true,
                ct);

            ScanCommand.RenderPreview(package.ProjectName, package.PackageName, package.PackageVersion, package.ScanResult);
            AnsiConsole.MarkupLine($"[blue]Archive SHA-256:[/] [green]{package.Sha256Hash}[/]");

            if (!package.ScanResult.IsValid)
            {
                AnsiConsole.MarkupLine("[red]Deploy cancelled.[/] Fix scan errors before upload.");
                return 1;
            }

            var token = ResolveToken(
                            settings.Token,
                            Environment.GetEnvironmentVariable("SERTO_API_TOKEN"),
                            Environment.GetEnvironmentVariable("IP_API_TOKEN"))
                        ?? AnsiConsole.Ask<string>("Enter your [green]API token[/]:");

            PackageUploadResponse? uploadResult = null;
            await AnsiConsole.Status()
                .StartAsync("Uploading to Control Plane...", async ctx =>
                {
                    using var client = new HttpClient();
                    client.BaseAddress = new Uri(settings.ControlPlaneUrl);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    using var content = new MultipartFormDataContent();
                    var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(package.ArchivePath, ct));
                    fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");

                    content.Add(new StringContent(package.PackageName), "name");
                    content.Add(new StringContent(package.PackageVersion), "version");
                    content.Add(fileContent, "file", Path.GetFileName(package.ArchivePath));

                    var response = await client.PostAsync("/api/integration-packages", content, ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync(ct);
                        throw new Exception($"Upload failed: {response.StatusCode} - {error}");
                    }

                    uploadResult = await response.Content.ReadFromJsonAsync<PackageUploadResponse>(cancellationToken: ct);
                });

            if (uploadResult is not null)
                RenderUploadResult(uploadResult);
            else
                AnsiConsole.MarkupLine("[green]Success![/] Package uploaded; the control plane will auto-provision discovered integrations.");
        }
        finally
        {
            if (package is not null && File.Exists(package.ArchivePath))
                File.Delete(package.ArchivePath);
        }

        return 0;
    }

    public static string? ResolveToken(string? explicitToken, params string?[] environmentTokens)
    {
        if (!string.IsNullOrWhiteSpace(explicitToken)) return explicitToken.Trim();

        foreach (var token in environmentTokens ?? [])
        {
            if (!string.IsNullOrWhiteSpace(token))
                return token.Trim();
        }

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

    public static void RenderUploadResult(PackageUploadResponse result)
    {
        AnsiConsole.MarkupLine($"[green]Success![/] Uploaded [green]{Markup.Escape(result.Package.Name)}[/] [blue]version:[/] [green]{Markup.Escape(result.Package.Version)}[/]");
        AnsiConsole.MarkupLine($"[blue]Package id:[/] [green]{result.Package.Id}[/]");
        AnsiConsole.MarkupLine($"[blue]Server SHA-256:[/] [green]{Markup.Escape(result.Package.Sha256Hash)}[/]");

        if (result.Provisioning.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No integrations were discovered by the control plane.[/]");
            return;
        }

        var integrations = new Table().Title("Provisioned integrations");
        integrations.AddColumn("Action");
        integrations.AddColumn("Integration");
        integrations.AddColumn("Slug");
        integrations.AddColumn("Class");
        integrations.AddColumn("Package");

        foreach (var item in result.Provisioning)
        {
            integrations.AddRow(
                Markup.Escape(item.Action),
                Markup.Escape(item.Name),
                Markup.Escape(item.Slug),
                Markup.Escape(item.ClassName),
                Markup.Escape(result.Package.Version));
        }

        AnsiConsole.Write(integrations);

        var triggers = new Table().Title("Provisioned triggers");
        triggers.AddColumn("Integration");
        triggers.AddColumn("Action");
        triggers.AddColumn("Trigger");
        triggers.AddColumn("Type");
        triggers.AddColumn("Runtime details");

        foreach (var item in result.Provisioning)
        {
            foreach (var trigger in item.Triggers)
            {
                triggers.AddRow(
                    Markup.Escape(item.Slug),
                    Markup.Escape(trigger.Action),
                    Markup.Escape(trigger.Slug),
                    Markup.Escape(trigger.Type),
                    Markup.Escape(FormatTriggerDetails(trigger)));
            }
        }

        AnsiConsole.Write(triggers);
    }

    public static string FormatTriggerDetails(PackageProvisionedTriggerResponse trigger)
    {
        if (trigger.Type.Equals("Scheduled", StringComparison.OrdinalIgnoreCase))
            return trigger.NextRunAt is null
                ? $"cron: {trigger.CronExpression ?? "not set"}"
                : $"cron: {trigger.CronExpression}, next: {trigger.NextRunAt:O}";

        if (trigger.Type.Equals("Webhook", StringComparison.OrdinalIgnoreCase))
        {
            var preserved = trigger.WebhookSecretPreserved ? ", secret preserved" : "";
            return $"{trigger.WebhookUrl ?? "webhook URL unavailable"}{preserved}";
        }

        return trigger.Enabled ? "enabled" : "disabled";
    }
}

public record PackageUploadResponse(
    PackageMetadataResponse Package,
    IReadOnlyList<PackageProvisioningResponse> Provisioning);

public record PackageMetadataResponse(
    Guid Id,
    string Name,
    string Version,
    string FileName,
    long SizeBytes,
    string Sha256Hash,
    DateTime CreatedAt);

public record PackageProvisioningResponse(
    Guid Id,
    string Name,
    string Slug,
    string Environment,
    string ClassName,
    string Action,
    Guid PackageId,
    IReadOnlyList<PackageProvisionedTriggerResponse> Triggers);

public record PackageProvisionedTriggerResponse(
    Guid Id,
    string Name,
    string Slug,
    string Type,
    bool Enabled,
    string Action,
    string? CronExpression = null,
    DateTime? NextRunAt = null,
    string? WebhookUrl = null,
    bool WebhookSecretPreserved = false);
