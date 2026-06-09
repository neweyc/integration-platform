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
    public void ResolvePackageVersion_FallsBackToCalendarVersion()
    {
        // The temp project lives outside any git working tree, so the fallback is the calendar
        // timestamp alone with no git enrichment.
        using var project = TemporaryProject("""
<Project Sdk="Microsoft.NET.Sdk">
</Project>
""");

        var version = DeployCommand.ResolvePackageVersion(project.Path, null, FixedNow);

        Assert.Equal("2026.06.03.123456", version);
    }

    [Fact]
    public void BuildAutoVersion_NoGit_IsTimestampOnly()
    {
        Assert.Equal("2026.06.03.123456", DeployCommand.BuildAutoVersion(FixedNow, null));
    }

    [Fact]
    public void BuildAutoVersion_CleanGitTree_AppendsShortSha()
    {
        var version = DeployCommand.BuildAutoVersion(FixedNow, new GitInfo("a1b2c3d", IsDirty: false));

        Assert.Equal("2026.06.03.123456-a1b2c3d", version);
    }

    [Fact]
    public void BuildAutoVersion_DirtyGitTree_AppendsShaAndDirtyMarker()
    {
        var version = DeployCommand.BuildAutoVersion(FixedNow, new GitInfo("a1b2c3d", IsDirty: true));

        Assert.Equal("2026.06.03.123456-a1b2c3d-dirty", version);
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

    [Fact]
    public void FormatTriggerDetails_ShowsCronOverrideDrift()
    {
        var details = DeployCommand.FormatTriggerDetails(new PackageProvisionedTriggerResponse(
            Guid.NewGuid(),
            "Schedule",
            "schedule",
            "Scheduled",
            Enabled: true,
            "Updated",
            CronExpression: "*/5 * * * *",
            NextRunAt: new DateTime(2026, 6, 7, 0, 5, 0, DateTimeKind.Utc),
            DeclaredCronExpression: "0 0 * * *",
            CronOverridden: true));

        Assert.Contains("cron: */5 * * * *", details);
        Assert.Contains("override preserved", details);
        Assert.Contains("code declares 0 0 * * *", details);
    }

    [Fact]
    public void FormatTriggerDetails_ShowsEnabledOverride()
    {
        var details = DeployCommand.FormatTriggerDetails(new PackageProvisionedTriggerResponse(
            Guid.NewGuid(),
            "Schedule",
            "schedule",
            "Scheduled",
            Enabled: false,
            "Updated",
            CronExpression: "0 0 * * *",
            EnabledOverridden: true));

        Assert.Contains("operator kept disabled", details);
    }

    [Fact]
    public void FormatSecretCheckLines_ReportsMissingSecrets()
    {
        var lines = DeployCommand.FormatSecretCheckLines(new PackageSecretCheckResponse(
            "production",
            Required: ["DB_CONNECTION_STRING", "ERP_API_KEY"],
            Satisfied: ["ERP_API_KEY"],
            Missing: ["DB_CONNECTION_STRING"]));

        Assert.Contains(lines, l => l.Contains("2 required") && l.Contains("1 configured") && l.Contains("1 missing"));
        Assert.Contains(lines, l => l.Contains("Missing in production") && l.Contains("DB_CONNECTION_STRING"));
    }

    [Fact]
    public void FormatSecretCheckLines_AllConfigured()
    {
        var lines = DeployCommand.FormatSecretCheckLines(new PackageSecretCheckResponse(
            "production",
            Required: ["ERP_API_KEY"],
            Satisfied: ["ERP_API_KEY"],
            Missing: []));

        Assert.Contains(lines, l => l.Contains("All required secrets are configured in production"));
        Assert.DoesNotContain(lines, l => l.Contains("Missing"));
    }

    [Fact]
    public void FormatSecretCheckLines_NoneRequired()
    {
        var lines = DeployCommand.FormatSecretCheckLines(new PackageSecretCheckResponse(
            "production", Required: [], Satisfied: [], Missing: []));

        var line = Assert.Single(lines);
        Assert.Contains("no secrets were detected", line);
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
