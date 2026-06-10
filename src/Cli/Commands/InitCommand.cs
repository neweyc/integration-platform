using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Cli.Commands;

public sealed class InitCommand : AsyncCommand<InitCommand.Settings>
{
    // The Serto.Sdk/Connectors/Testing version scaffolded projects reference. Bump this to the
    // current release whenever those packages are published so `serto init` pins a real, recent version.
    private const string SertoPackageVersion = "1.2.0";
    private const string TestSdkVersion = "17.14.1";
    private const string XunitVersion = "2.9.3";
    private const string XunitRunnerVersion = "3.1.4";

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[ProjectName]")]
        [Description("The name of the integration project to create")]
        public string? ProjectName { get; init; }

        [CommandOption("-t|--template")]
        [Description("Which starter to scaffold: 'scheduled' (default) or 'webhook'")]
        [DefaultValue("scheduled")]
        public string Template { get; init; } = "scheduled";
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        var projectName = settings.ProjectName
            ?? AnsiConsole.Ask<string>("What is the [green]name[/] of your integration project?");

        var template = settings.Template.Trim().ToLowerInvariant();
        if (template is not ("scheduled" or "webhook"))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Unknown template '[yellow]{Markup.Escape(settings.Template)}[/]'. Use 'scheduled' or 'webhook'.");
            return Task.FromResult(1);
        }

        // Reject names that would escape the target directory or produce an unusable project. The C#
        // namespace is derived separately (sanitized) so a name like "my-app" still works.
        if (!IsValidProjectName(projectName))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] '[yellow]{Markup.Escape(projectName)}[/]' is not a valid project name. Use letters, numbers, '_', '-', or '.' and no path separators.");
            return Task.FromResult(1);
        }

        var namespaceName = ToNamespace(projectName);
        var dir = Path.Combine(Directory.GetCurrentDirectory(), projectName);
        if (Directory.Exists(dir))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Directory [yellow]{Markup.Escape(projectName)}[/] already exists.");
            return Task.FromResult(1);
        }

        AnsiConsole.Status().Start($"Creating integration project [green]{projectName}[/]...",
            _ => Scaffold(dir, projectName, namespaceName, template));

        AnsiConsole.MarkupLine($"[green]Success![/] Integration project [blue]{Markup.Escape(projectName)}[/] created ([yellow]{template}[/] template).");
        AnsiConsole.MarkupLine("Next steps:");
        AnsiConsole.MarkupLine($"  1. [yellow]cd {Markup.Escape(projectName)}[/]");
        AnsiConsole.MarkupLine("  2. [yellow]dotnet build[/]");
        AnsiConsole.MarkupLine($"  3. [yellow]dotnet test {Markup.Escape(projectName)}.Tests[/]  (run the example unit test)");
        AnsiConsole.MarkupLine("  4. [yellow]serto test[/]  (validate + run locally; [yellow]serto dev[/] to watch on save)");
        AnsiConsole.MarkupLine("  5. [yellow]serto login --url <control-plane>[/] then [yellow]serto deploy[/]");

        return Task.FromResult(0);
    }

    /// <summary>Writes the full scaffold (integration project + test project + docs) into <paramref name="dir"/>.</summary>
    public static void Scaffold(string dir, string projectName, string namespaceName, string template)
    {
        var testDir = Path.Combine(dir, $"{projectName}.Tests");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(testDir);

        File.WriteAllText(Path.Combine(dir, $"{projectName}.csproj"), BuildProjectFile());
        File.WriteAllText(Path.Combine(dir, "MyIntegration.cs"), BuildIntegrationClass(namespaceName, template));
        File.WriteAllText(Path.Combine(dir, "README.md"), BuildReadme(projectName, template));
        File.WriteAllText(Path.Combine(dir, ".secrets.example.json"), BuildSecretsExample());
        File.WriteAllText(Path.Combine(dir, ".gitignore"), BuildGitignore());

        File.WriteAllText(Path.Combine(testDir, $"{projectName}.Tests.csproj"), BuildTestProjectFile(projectName));
        File.WriteAllText(Path.Combine(testDir, "MyIntegrationTests.cs"), BuildExampleTest(namespaceName, template));
    }

    // A project name must not escape the target directory or contain filename-illegal characters. The C#
    // namespace is derived separately via ToNamespace, so name styles like "my-app" are still allowed.
    public static bool IsValidProjectName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && !name.Contains("..", StringComparison.Ordinal)
        && System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9_][A-Za-z0-9_.-]*$");

    /// <summary>Derives a legal C# namespace from a project name (e.g. "my-app" → "my_app", "123Foo" → "_123Foo").</summary>
    public static string ToNamespace(string projectName)
    {
        var segments = projectName
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeIdentifier);
        return string.Join('.', segments);
    }

    private static string SanitizeIdentifier(string segment)
    {
        var identifier = new string(segment.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        if (identifier.Length == 0 || char.IsDigit(identifier[0]))
            identifier = "_" + identifier;
        return identifier;
    }

    public static string BuildProjectFile() =>
        $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Serto.Sdk" Version="{{SertoPackageVersion}}" />
            <PackageReference Include="Serto.Connectors" Version="{{SertoPackageVersion}}" />
          </ItemGroup>
        </Project>
        """;

    public static string BuildIntegrationClass(string namespaceName, string template) =>
        template == "webhook"
            ? $$"""
            using Serto.Sdk;
            using Microsoft.Extensions.Logging;

            namespace {{namespaceName}};

            [WebhookIntegration("Sample Webhook", "sample-webhook")]
            public class MyIntegration : IIntegration
            {
                public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
                {
                    // The raw request body is delivered as context.Payload.
                    context.Logger.LogInformation("Received webhook payload: {Payload}", context.Payload);

                    await Task.CompletedTask;
                }
            }
            """
            : $$"""
            using Serto.Sdk;
            using Serto.Connectors.Http;
            using Microsoft.Extensions.Logging;

            namespace {{namespaceName}};

            [ScheduledIntegration("Sample Integration", "sample-integration", "0 * * * *")]
            public class MyIntegration : IIntegration
            {
                public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
                {
                    context.Logger.LogInformation("Hello from {IntegrationName}!", context.Execution.IntegrationName);

                    // Call an external API. Uncomment and add EXAMPLE_API_TOKEN to your secrets:
                    // var api = context.HttpConnector("https://api.example.com").WithBearerToken("EXAMPLE_API_TOKEN");
                    // var data = await api.GetJsonAsync<MyData>("/data", ct);

                    await Task.CompletedTask;
                    context.Logger.LogInformation("Done.");
                }
            }
            """;

    public static string BuildTestProjectFile(string projectName) =>
        $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <IsPackable>false</IsPackable>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="{{TestSdkVersion}}" />
            <PackageReference Include="xunit" Version="{{XunitVersion}}" />
            <PackageReference Include="xunit.runner.visualstudio" Version="{{XunitRunnerVersion}}" />
            <PackageReference Include="Serto.Testing" Version="{{SertoPackageVersion}}" />
          </ItemGroup>

          <ItemGroup>
            <ProjectReference Include="..\{{projectName}}.csproj" />
          </ItemGroup>
        </Project>
        """;

    public static string BuildExampleTest(string namespaceName, string template)
    {
        var arrange = template == "webhook"
            ? "payload: \"{\\\"event\\\":\\\"test\\\"}\""
            : "secrets: new Dictionary<string, string>()";

        return $$"""
        using Serto.Testing;
        using Xunit;
        using {{namespaceName}};

        namespace {{namespaceName}}.Tests;

        public class MyIntegrationTests
        {
            [Fact]
            public async Task RunAsync_CompletesWithoutError()
            {
                // IntegrationTester builds a TestIntegrationContext and runs your integration.
                // For richer setups use new TestContextBuilder()... or pass a fake HttpClient via TestHttp.
                await IntegrationTester.RunAsync<MyIntegration>(
                    {{arrange}});
            }
        }
        """;
    }

    public static string BuildReadme(string projectName, string template) =>
        $$"""
        # {{projectName}}

        A Serto integration ({{template}} template).

        ## Develop

        - `dotnet build` — compile the integration.
        - `serto test` — validate (attribute, cron, constructor, required secrets) and run it locally.
        - `serto dev` — re-validate and re-run on every file save.
        - `dotnet test {{projectName}}.Tests` — run the unit tests.

        ## Secrets

        Your integration reads secrets by name from `context.Secrets`. For local runs, copy
        `.secrets.example.json` to `secrets.json`, fill in real values, and pass it:

            serto test --secrets secrets.json

        `serto scan` lists the secret names your code references. `secrets.json` is git-ignored — never
        commit real secret values.

        ## The integration context

        `RunAsync(IIntegrationContext context, CancellationToken ct)` gives you:

        - `context.Secrets` — configured secret values, by name.
        - `context.Logger` — structured logging.
        - `context.Http` — a raw `HttpClient` (or use `context.HttpConnector(...)` from `Serto.Connectors`).
        - `context.Payload` — the raw request body, for webhook integrations.
        - `context.Execution` — execution and environment metadata.

        ## Deploy

            serto login --url <control-plane-url>
            serto deploy
        """;

    public static string BuildSecretsExample() =>
        """
        {
          "EXAMPLE_API_TOKEN": "replace-with-your-token"
        }
        """;

    public static string BuildGitignore() =>
        """
        bin/
        obj/
        *.user
        secrets.json
        """;
}
