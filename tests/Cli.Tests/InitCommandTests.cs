using Cli.Commands;

namespace Cli.Tests;

public class InitCommandTests
{
    [Fact]
    public void ScheduledTemplate_HasScheduledAttributeAndCron()
    {
        var code = InitCommand.BuildIntegrationClass("Acme", "scheduled");

        Assert.Contains("[ScheduledIntegration(", code, StringComparison.Ordinal);
        Assert.Contains("0 * * * *", code, StringComparison.Ordinal);
        Assert.Contains("namespace Acme;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WebhookTemplate_UsesWebhookAttributeAndPayload()
    {
        var code = InitCommand.BuildIntegrationClass("Acme", "webhook");

        Assert.Contains("[WebhookIntegration(", code, StringComparison.Ordinal);
        Assert.Contains("context.Payload", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduledIntegration", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectFile_ReferencesSertoPackages()
    {
        var csproj = InitCommand.BuildProjectFile();

        Assert.Contains("Serto.Sdk", csproj, StringComparison.Ordinal);
        Assert.Contains("Serto.Connectors", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void TestProject_ReferencesTestingAndIntegrationProject()
    {
        var csproj = InitCommand.BuildTestProjectFile("Acme");

        Assert.Contains("Serto.Testing", csproj, StringComparison.Ordinal);
        Assert.Contains("xunit", csproj, StringComparison.Ordinal);
        Assert.Contains(@"..\Acme.csproj", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void ExampleTest_Scheduled_ReferencesIntegrationAndUsesSecrets()
    {
        var code = InitCommand.BuildExampleTest("Acme", "scheduled");

        Assert.Contains("using Acme;", code, StringComparison.Ordinal);
        Assert.Contains("IntegrationTester.RunAsync<MyIntegration>", code, StringComparison.Ordinal);
        Assert.Contains("secrets:", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ExampleTest_Webhook_PassesPayload()
    {
        var code = InitCommand.BuildExampleTest("Acme", "webhook");

        Assert.Contains("payload:", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_MentionsKeyCommands()
    {
        var readme = InitCommand.BuildReadme("Acme", "scheduled");

        Assert.Contains("serto test", readme, StringComparison.Ordinal);
        Assert.Contains("serto deploy", readme, StringComparison.Ordinal);
        Assert.Contains("dotnet test Acme.Tests", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Gitignore_IgnoresSecretsJson()
    {
        Assert.Contains("secrets.json", InitCommand.BuildGitignore(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AcmeSync", true)]
    [InlineData("my-app", true)]
    [InlineData("Acme.Sync", true)]
    [InlineData("123Foo", true)]      // valid name; namespace is sanitized separately
    [InlineData("../AcmeSync", false)] // path traversal
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("-leading", false)]
    [InlineData("", false)]
    public void IsValidProjectName_AcceptsSafeNamesAndRejectsPathsAndJunk(string name, bool expected)
    {
        Assert.Equal(expected, InitCommand.IsValidProjectName(name));
    }

    [Theory]
    [InlineData("AcmeSync", "AcmeSync")]
    [InlineData("my-app", "my_app")]
    [InlineData("123Foo", "_123Foo")]
    [InlineData("Acme.Sync", "Acme.Sync")]
    [InlineData("Acme.2nd", "Acme._2nd")]
    public void ToNamespace_ProducesLegalIdentifiers(string name, string expected)
    {
        Assert.Equal(expected, InitCommand.ToNamespace(name));
    }

    [Fact]
    public void SanitizedName_FlowsIntoGeneratedNamespace()
    {
        var ns = InitCommand.ToNamespace("my-app");
        var code = InitCommand.BuildIntegrationClass(ns, "scheduled");

        Assert.Contains("namespace my_app;", code, StringComparison.Ordinal);
    }
}
