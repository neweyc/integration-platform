using System.ComponentModel;
using System.Diagnostics;
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

            // Precedence: explicit flag → env vars (CI) → saved login credentials → interactive prompt.
            var token = ResolveToken(
                settings.Token,
                Environment.GetEnvironmentVariable("SERTO_API_TOKEN"),
                Environment.GetEnvironmentVariable("IP_API_TOKEN"));

            if (token is null)
            {
                token = new CredentialStore().GetToken(settings.ControlPlaneUrl);
                if (token is not null)
                    AnsiConsole.MarkupLine($"[grey]Using saved credentials for {Markup.Escape(settings.ControlPlaneUrl)}.[/]");
            }

            token ??= AnsiConsole.Ask<string>("Enter your [green]API token[/]:");

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

                    // The server can't detect required secrets from a compiled assembly, so send the
                    // names found by the local source scan for it to check against configured secrets.
                    if (package.ScanResult.RequiredSecrets.Count > 0)
                        content.Add(
                            new StringContent(string.Join(",", package.ScanResult.RequiredSecrets)),
                            "requiredSecrets");

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

        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(csprojPath))!;
        return BuildAutoVersion(now, GitInfo.Detect(projectDirectory));
    }

    // Builds the auto-generated package version when the project declares none: a readable, sortable
    // calendar timestamp, optionally enriched with the git short SHA (and a -dirty marker) when the
    // project sits in a git working tree. Git is never required — without a repo the timestamp alone
    // is used. Internal git info is passed in so the formatting is tested without depending on whether
    // the test host happens to be a git repository.
    public static string BuildAutoVersion(DateTimeOffset now, GitInfo? git)
    {
        var version = now.UtcDateTime.ToString("yyyy.MM.dd.HHmmss");

        if (git is not null && !string.IsNullOrEmpty(git.ShortSha))
        {
            version += "-" + git.ShortSha;
            if (git.IsDirty)
                version += "-dirty";
        }

        return version;
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
            // Still surface the secret check — a package can require secrets even with nothing provisioned.
            if (result.SecretCheck is { } noProvisioningSecretCheck)
                RenderSecretCheck(noProvisioningSecretCheck);
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

        if (result.SecretCheck is { } secretCheck)
            RenderSecretCheck(secretCheck);
    }

    public static void RenderSecretCheck(PackageSecretCheckResponse check)
    {
        var color = check.Required.Count == 0 ? "grey"
            : check.Missing.Count == 0 ? "green"
            : "yellow";

        foreach (var line in FormatSecretCheckLines(check))
            AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(line)}[/]");
    }

    // Pure summary of the server's secret check, kept separate from rendering so it can be tested.
    public static IReadOnlyList<string> FormatSecretCheckLines(PackageSecretCheckResponse check)
    {
        if (check.Required.Count == 0)
            return ["Secret check: no secrets were detected as required by the scanned source."];

        var lines = new List<string>
        {
            $"Secret check (environment: {check.Environment}): " +
            $"{check.Required.Count} required, {check.Satisfied.Count} configured, {check.Missing.Count} missing"
        };

        lines.Add(check.Missing.Count == 0
            ? $"All required secrets are configured in {check.Environment}."
            : $"Missing in {check.Environment}: {string.Join(", ", check.Missing)} — integrations needing these will fail until they are set.");

        return lines;
    }

    public static string FormatTriggerDetails(PackageProvisionedTriggerResponse trigger)
    {
        var detail = FormatBaseTriggerDetails(trigger);

        // Surface operator overrides preserved across this redeploy as drift the operator can reconcile.
        var overrides = new List<string>();
        if (trigger.CronOverridden)
            overrides.Add($"cron override preserved (code declares {trigger.DeclaredCronExpression ?? "none"})");
        if (trigger.EnabledOverridden)
            overrides.Add($"operator kept {(trigger.Enabled ? "enabled" : "disabled")}");

        return overrides.Count == 0 ? detail : $"{detail} — {string.Join("; ", overrides)}";
    }

    private static string FormatBaseTriggerDetails(PackageProvisionedTriggerResponse trigger)
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

// Optional git enrichment for auto-generated package versions. Detection never throws and never
// requires git: it returns null when the directory is not in a working tree, git is not installed,
// or HEAD cannot be resolved (e.g. a repo with no commits yet).
public sealed record GitInfo(string ShortSha, bool IsDirty)
{
    public static GitInfo? Detect(string directory)
    {
        var sha = RunGit(directory, "rev-parse --short HEAD");
        if (string.IsNullOrWhiteSpace(sha))
            return null;

        // Limit the dirty check to the project directory so unrelated changes elsewhere in a monorepo
        // do not flag this package as dirty. Any reported change (tracked or untracked) counts.
        var status = RunGit(directory, "status --porcelain .");
        return new GitInfo(sha.Trim(), !string.IsNullOrWhiteSpace(status));
    }

    private static string? RunGit(string workingDirectory, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return null;

            // Read stdout asynchronously so a large status listing cannot fill the pipe buffer and
            // deadlock against WaitForExit, which is bounded so a hung git can never block a deploy.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return null;
            }

            return process.ExitCode == 0 ? stdoutTask.GetAwaiter().GetResult() : null;
        }
        catch
        {
            // git not installed, not a repo, or any other failure — fall back to a timestamp-only version.
            return null;
        }
    }
}

public record PackageUploadResponse(
    PackageMetadataResponse Package,
    IReadOnlyList<PackageProvisioningResponse> Provisioning,
    PackageSecretCheckResponse? SecretCheck = null);

public record PackageSecretCheckResponse(
    string Environment,
    IReadOnlyList<string> Required,
    IReadOnlyList<string> Satisfied,
    IReadOnlyList<string> Missing);

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
    bool WebhookSecretPreserved = false,
    string? DeclaredCronExpression = null,
    bool CronOverridden = false,
    bool EnabledOverridden = false);
