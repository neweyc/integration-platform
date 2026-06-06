using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Cli.Commands;

public sealed class PackageCommand : AsyncCommand<PackageCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-p|--project")]
        [Description("Path to the integration .csproj. Defaults to the first .csproj in the current directory.")]
        public string? ProjectPath { get; init; }

        [CommandOption("-v|--version")]
        [Description("Package version. Defaults to PackageVersion/Version from the project file.")]
        public string? Version { get; init; }

        [CommandOption("-n|--name")]
        [Description("Package name. Defaults to the project name.")]
        public string? PackageName { get; init; }

        [CommandOption("-o|--output")]
        [Description("Output directory for the package archive.")]
        public string? OutputDirectory { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        var csprojFile = ScanCommand.ResolveProjectPath(settings.ProjectPath, Directory.GetCurrentDirectory());
        if (csprojFile is null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] No .csproj file found.");
            return 1;
        }

        var package = await CreateAsync(
            csprojFile,
            settings.PackageName,
            settings.Version,
            settings.OutputDirectory,
            keepArchive: true,
            ct);

        ScanCommand.RenderPreview(package.ProjectName, package.PackageName, package.PackageVersion, package.ScanResult);

        if (!package.ScanResult.IsValid)
        {
            AnsiConsole.MarkupLine("[red]Package not created.[/] Fix scan errors before packaging.");
            if (File.Exists(package.ArchivePath))
                File.Delete(package.ArchivePath);
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Package created:[/] {Markup.Escape(package.ArchivePath)}");
        AnsiConsole.MarkupLine($"[blue]SHA-256:[/] [green]{package.Sha256Hash}[/]");
        return 0;
    }

    public static async Task<PackageBuildResult> CreateAsync(
        string csprojFile,
        string? packageName,
        string? packageVersion,
        string? outputDirectory,
        bool keepArchive,
        CancellationToken ct)
    {
        csprojFile = Path.GetFullPath(csprojFile);
        var projectDirectory = Path.GetDirectoryName(csprojFile)!;
        var projectName = Path.GetFileNameWithoutExtension(csprojFile);
        var resolvedPackageName = string.IsNullOrWhiteSpace(packageName) ? projectName : packageName.Trim();
        var resolvedPackageVersion = DeployCommand.ResolvePackageVersion(csprojFile, packageVersion, DateTimeOffset.UtcNow);
        var publishDir = Path.Combine(projectDirectory, "publish");
        var resolvedOutputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? projectDirectory
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(resolvedOutputDirectory);

        var archivePath = Path.Combine(
            resolvedOutputDirectory,
            DeployCommand.CreatePackageArchiveFileName(resolvedPackageName, resolvedPackageVersion));

        try
        {
            if (Directory.Exists(publishDir))
                Directory.Delete(publishDir, recursive: true);

            await PublishAsync(csprojFile, publishDir, ct);

            if (File.Exists(archivePath))
                File.Delete(archivePath);

            ZipFile.CreateFromDirectory(publishDir, archivePath);

            var scan = ScanCommand.ScanDirectory(publishDir)
                .WithRequiredSecrets(ScanCommand.DiscoverRequiredSecrets(projectDirectory));
            var hash = await ComputeSha256Async(archivePath, ct);

            return new PackageBuildResult(
                projectName,
                resolvedPackageName,
                resolvedPackageVersion,
                archivePath,
                publishDir,
                hash,
                scan);
        }
        finally
        {
            if (Directory.Exists(publishDir))
                Directory.Delete(publishDir, recursive: true);

            if (!keepArchive && File.Exists(archivePath))
                File.Delete(archivePath);
        }
    }

    private static async Task PublishAsync(string csprojFile, string publishDir, CancellationToken ct)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{csprojFile}\" -c Release -o \"{publishDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process is null)
            throw new InvalidOperationException("Failed to start dotnet publish.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Publish failed: {stderr}{Environment.NewLine}{stdout}");
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public record PackageBuildResult(
    string ProjectName,
    string PackageName,
    string PackageVersion,
    string ArchivePath,
    string PublishDirectory,
    string Sha256Hash,
    ScanResult ScanResult);
