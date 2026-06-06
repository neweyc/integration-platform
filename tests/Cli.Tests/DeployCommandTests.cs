using Cli.Commands;

namespace Cli.Tests;

public class DeployCommandTests
{
    [Fact]
    public void ResolveToken_PrefersExplicitToken()
    {
        var token = DeployCommand.ResolveToken(" explicit-token ", "env-token");

        Assert.Equal("explicit-token", token);
    }

    [Fact]
    public void ResolveToken_UsesEnvironmentTokenWhenExplicitTokenMissing()
    {
        var token = DeployCommand.ResolveToken(null, " env-token ");

        Assert.Equal("env-token", token);
    }

    [Fact]
    public void ResolveToken_ReturnsNullWhenNoTokenAvailable()
    {
        var token = DeployCommand.ResolveToken(" ", (string?)null);

        Assert.Null(token);
    }

    [Fact]
    public void ResolvePackageVersion_PrefersExplicitVersion()
    {
        using var project = TemporaryProject("""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
""");

        var version = DeployCommand.ResolvePackageVersion(project.Path, " 9.9.9 ", FixedNow);

        Assert.Equal("9.9.9", version);
    }

    [Fact]
    public void ResolvePackageVersion_UsesPackageVersionFromProjectFile()
    {
        using var project = TemporaryProject("""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Version>1.2.3</Version>
    <PackageVersion>2.3.4</PackageVersion>
  </PropertyGroup>
</Project>
""");

        var version = DeployCommand.ResolvePackageVersion(project.Path, null, FixedNow);

        Assert.Equal("2.3.4", version);
    }

    [Fact]
    public void ResolvePackageVersion_UsesVersionFromProjectFile()
    {
        using var project = TemporaryProject("""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
""");

        var version = DeployCommand.ResolvePackageVersion(project.Path, null, FixedNow);

        Assert.Equal("1.2.3", version);
    }

    [Fact]
    public void ResolvePackageVersion_FallsBackToTimestampedDevelopmentVersion()
    {
        using var project = TemporaryProject("""
<Project Sdk="Microsoft.NET.Sdk">
</Project>
""");

        var version = DeployCommand.ResolvePackageVersion(project.Path, null, FixedNow);

        Assert.Equal("0.1.0-dev.20260603123456", version);
    }

    [Fact]
    public void CreatePackageArchiveFileName_ReplacesPathSeparators()
    {
        var fileName = DeployCommand.CreatePackageArchiveFileName("customer/integrations", "1.0\\rollback");

        Assert.Equal("customer-integrations.1.0-rollback.zip", fileName);
    }

    [Fact]
    public void FormatTriggerDetails_IncludesScheduleNextRun()
    {
        var details = DeployCommand.FormatTriggerDetails(new PackageProvisionedTriggerResponse(
            Guid.NewGuid(),
            "Every Five",
            "every-five",
            "Scheduled",
            Enabled: true,
            "Created",
            CronExpression: "*/5 * * * *",
            NextRunAt: new DateTime(2026, 6, 5, 12, 5, 0, DateTimeKind.Utc)));

        Assert.Contains("cron: */5 * * * *", details);
        Assert.Contains("2026-06-05T12:05:00", details);
    }

    [Fact]
    public void FormatTriggerDetails_IncludesWebhookSecretPreservation()
    {
        var details = DeployCommand.FormatTriggerDetails(new PackageProvisionedTriggerResponse(
            Guid.NewGuid(),
            "Hook",
            "hook",
            "Webhook",
            Enabled: true,
            "Updated",
            WebhookUrl: "/webhooks/acme/order-sync/hook",
            WebhookSecretPreserved: true));

        Assert.Equal("/webhooks/acme/order-sync/hook, secret preserved", details);
    }

    private static DateTimeOffset FixedNow => new(2026, 6, 3, 12, 34, 56, TimeSpan.Zero);

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
            if (File.Exists(Path)) File.Delete(Path);
        }
    }
}
