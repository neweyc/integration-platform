using Cli.Commands;
using Serto.Sdk;

namespace Cli.Tests;

public class ScanCommandTests
{
    [Fact]
    public void ResolveProjectPath_UsesExplicitPath()
    {
        var path = ScanCommand.ResolveProjectPath("project.csproj", Directory.GetCurrentDirectory());

        Assert.EndsWith("project.csproj", path);
    }

    [Fact]
    public void ResolveProjectPath_FindsProjectInCurrentDirectory()
    {
        using var directory = new TemporaryDirectory();
        var projectPath = Path.Combine(directory.Path, "Sample.csproj");
        File.WriteAllText(projectPath, "<Project />");

        var path = ScanCommand.ResolveProjectPath(null, directory.Path);

        Assert.Equal(projectPath, path);
    }

    [Fact]
    public void ScanAssembly_DiscoversBaseIntegrationMetadata()
    {
        var result = ScanCommand.ScanAssembly(typeof(BaseOnlyIntegration).Assembly);

        var integration = result.Single(i => i.Slug == "cli-base-only");

        Assert.Equal("CLI Base Only", integration.Name);
        Assert.Equal(typeof(BaseOnlyIntegration).FullName, integration.ClassName);
        Assert.Equal("No stored triggers", integration.Description);
        Assert.Equal(90, integration.TimeoutSeconds);
        Assert.Empty(integration.Triggers);
    }

    [Fact]
    public void ResolveTargetFramework_UsesSingleTargetFramework()
    {
        using var project = TemporaryProject("""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
""");

        var targetFramework = ScanCommand.ResolveTargetFramework(project.Path);

        Assert.Equal("net10.0", targetFramework);
    }

    [Fact]
    public void ResolveTargetFramework_UsesFirstMultiTargetFramework()
    {
        using var project = TemporaryProject("""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net10.0;net9.0</TargetFrameworks>
  </PropertyGroup>
</Project>
""");

        var targetFramework = ScanCommand.ResolveTargetFramework(project.Path);

        Assert.Equal("net10.0", targetFramework);
    }

    [Fact]
    public void ScanAssembly_DiscoversScheduledAndWebhookTriggerDeclarations()
    {
        var result = ScanCommand.ScanAssembly(typeof(MultiTriggerIntegration).Assembly);

        var integration = result.Single(i => i.Slug == "cli-multi-trigger");

        Assert.Equal("CLI Multi Trigger", integration.Name);
        Assert.Equal(typeof(MultiTriggerIntegration).FullName, integration.ClassName);
        Assert.Equal(300, integration.TimeoutSeconds);
        Assert.Equal(2, integration.RetryMaxAttempts);
        Assert.Equal(60, integration.RetryBackoffSeconds);
        Assert.Contains(integration.Triggers, t =>
            t.Name == "Scheduled"
            && t.Slug == "scheduled"
            && t.Type == "Scheduled"
            && t.CronExpression == "0 * * * *");
        Assert.Contains(integration.Triggers, t =>
            t.Name == "Webhook"
            && t.Slug == "webhook"
            && t.Type == "Webhook"
            && t.CronExpression is null);
    }

    [Fact]
    public void ScanDirectory_ReportsInvalidCron()
    {
        var assemblyPath = typeof(InvalidCronIntegration).Assembly.Location;
        using var directory = new TemporaryDirectory();
        File.Copy(assemblyPath, Path.Combine(directory.Path, Path.GetFileName(assemblyPath)));

        var result = ScanCommand.ScanDirectory(directory.Path);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'not-a-cron' is not a valid cron expression"));
    }

    [Fact]
    public void DiscoverRequiredSecrets_FindsConnectorAndContextSecretReferences()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "Integration.cs"), """
using Serto.Connectors.Http;
using Serto.Connectors.Sql;

public class Integration
{
    public void Run(dynamic context)
    {
        _ = context.Secrets["DIRECT_SECRET"];
        _ = context.Secrets.TryGetValue("TRY_GET_SECRET", out var value);
        _ = context.HttpConnector("https://example.com").WithBearerToken("HTTP_SECRET");
        _ = context.SqlConnector("SQL_SECRET");
    }
}
""");

        var secrets = ScanCommand.DiscoverRequiredSecrets(directory.Path);

        Assert.Equal(
            ["DIRECT_SECRET", "HTTP_SECRET", "SQL_SECRET", "TRY_GET_SECRET"],
            secrets);
    }

    [Integration("CLI Base Only", "cli-base-only", Description = "No stored triggers", TimeoutSeconds = 90)]
    private sealed class BaseOnlyIntegration : IIntegration
    {
        public Task RunAsync(IIntegrationContext context, CancellationToken ct) => Task.CompletedTask;
    }

    [ScheduledIntegration(
        "CLI Multi Trigger",
        "cli-multi-trigger",
        "0 * * * *",
        Description = "Runs hourly or by webhook.",
        TimeoutSeconds = 300,
        RetryMaxAttempts = 2,
        RetryBackoffSeconds = 60)]
    [WebhookIntegration(
        "CLI Multi Trigger",
        "cli-multi-trigger",
        Description = "Runs hourly or by webhook.",
        TimeoutSeconds = 300,
        RetryMaxAttempts = 2,
        RetryBackoffSeconds = 60)]
    private sealed class MultiTriggerIntegration : IIntegration
    {
        public Task RunAsync(IIntegrationContext context, CancellationToken ct) => Task.CompletedTask;
    }

    [ScheduledIntegration("Invalid Cron", "invalid-cron", "not-a-cron")]
    private sealed class InvalidCronIntegration : IIntegration
    {
        public Task RunAsync(IIntegrationContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    private static TemporaryProjectFile TemporaryProject(string contents)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".csproj");
        File.WriteAllText(path, contents);
        return new TemporaryProjectFile(path);
    }

    private sealed class TemporaryProjectFile(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
    }
}
