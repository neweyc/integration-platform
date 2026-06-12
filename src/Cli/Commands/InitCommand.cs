using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Cli.Commands;

public sealed class InitCommand : AsyncCommand<InitCommand.Settings>
{
    // The Serto.Sdk/Connectors/Testing version scaffolded projects reference. Bump this to the
    // current release whenever those packages are published so `serto init` pins a real, recent version.
    private const string SertoPackageVersion = "1.5.1";
    private const string TestSdkVersion = "17.14.1";
    private const string XunitVersion = "2.9.3";
    private const string XunitRunnerVersion = "3.1.4";

    // The Serto Go SDK module a scaffolded Go integration imports.
    private const string SertoGoModule = "github.com/neweyc/integration-platform/sdks/go/serto";
    private const string SertoGoVersion = "v1.5.1";

    // The Serto Node SDK package + version a scaffolded Node integration depends on.
    private const string SertoNodePackage = "@craytech/serto";
    private const string SertoNodeVersion = "^1.5.1";

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[ProjectName]")]
        [Description("The name of the integration project to create")]
        public string? ProjectName { get; init; }

        [CommandOption("-t|--template")]
        [Description("Which starter to scaffold: 'scheduled' (default) or 'webhook' (.NET only)")]
        [DefaultValue("scheduled")]
        public string Template { get; init; } = "scheduled";

        [CommandOption("-r|--runtime")]
        [Description("Integration runtime: 'dotnet' (default), 'python', 'node', 'go', or 'shell'")]
        [DefaultValue("dotnet")]
        public string Runtime { get; init; } = "dotnet";
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        var projectName = settings.ProjectName
            ?? AnsiConsole.Ask<string>("What is the [green]name[/] of your integration project?");

        var runtime = settings.Runtime.Trim().ToLowerInvariant();
        if (runtime is not ("dotnet" or "python" or "node" or "go" or "shell"))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Unsupported runtime '[yellow]{Markup.Escape(settings.Runtime)}[/]'. Use 'dotnet', 'python', 'node', 'go', or 'shell'.");
            return Task.FromResult(1);
        }

        var template = settings.Template.Trim().ToLowerInvariant();
        if (runtime == "dotnet" && template is not ("scheduled" or "webhook"))
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

        var dir = Path.Combine(Directory.GetCurrentDirectory(), projectName);
        if (Directory.Exists(dir))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Directory [yellow]{Markup.Escape(projectName)}[/] already exists.");
            return Task.FromResult(1);
        }

        if (runtime == "python")
        {
            AnsiConsole.Status().Start($"Creating Python integration [green]{projectName}[/]...",
                _ => ScaffoldPython(dir, projectName));

            AnsiConsole.MarkupLine($"[green]Success![/] Python integration [blue]{Markup.Escape(projectName)}[/] created.");
            AnsiConsole.MarkupLine("Next steps:");
            AnsiConsole.MarkupLine($"  1. [yellow]cd {Markup.Escape(projectName)}[/]");
            AnsiConsole.MarkupLine("  2. [yellow]pip install serto-sdk[/]  (the Serto Python SDK; import name is [yellow]serto[/])");
            AnsiConsole.MarkupLine("  3. Edit [yellow]main.py[/] and declare triggers/secrets in [yellow]serto.json[/].");
            AnsiConsole.MarkupLine("  4. [yellow]serto scan[/]  (preview what will be provisioned)");
            AnsiConsole.MarkupLine("  5. [yellow]serto login --url <control-plane>[/] then [yellow]serto deploy[/]");
            return Task.FromResult(0);
        }

        if (runtime == "node")
        {
            AnsiConsole.Status().Start($"Creating Node.js integration [green]{projectName}[/]...",
                _ => ScaffoldNode(dir, projectName));

            AnsiConsole.MarkupLine($"[green]Success![/] Node.js integration [blue]{Markup.Escape(projectName)}[/] created.");
            AnsiConsole.MarkupLine("Next steps:");
            AnsiConsole.MarkupLine($"  1. [yellow]cd {Markup.Escape(projectName)}[/]");
            AnsiConsole.MarkupLine("  2. [yellow]npm install[/]  (fetch the Serto Node SDK)");
            AnsiConsole.MarkupLine("  3. Edit [yellow]index.js[/]; declare triggers/secrets in [yellow]serto.json[/].");
            AnsiConsole.MarkupLine("  4. [yellow]serto scan[/]  (preview what will be provisioned)");
            AnsiConsole.MarkupLine("  5. [yellow]serto login --url <control-plane>[/] then [yellow]serto deploy[/]");
            return Task.FromResult(0);
        }

        if (runtime == "go")
        {
            AnsiConsole.Status().Start($"Creating Go integration [green]{projectName}[/]...",
                _ => ScaffoldGo(dir, projectName));

            AnsiConsole.MarkupLine($"[green]Success![/] Go integration [blue]{Markup.Escape(projectName)}[/] created (containerized).");
            AnsiConsole.MarkupLine("Go integrations run as a container image. Next steps:");
            AnsiConsole.MarkupLine($"  1. [yellow]cd {Markup.Escape(projectName)}[/]");
            AnsiConsole.MarkupLine("  2. [yellow]go mod tidy[/]  (fetch the Serto Go SDK)");
            AnsiConsole.MarkupLine("  3. Edit [yellow]main.go[/]; declare triggers/secrets in [yellow]serto.json[/].");
            AnsiConsole.MarkupLine("  4. Build & push the image: [yellow]docker build -t <registry>/<name>:tag .[/] then [yellow]docker push <registry>/<name>:tag[/]");
            AnsiConsole.MarkupLine("  5. Set that image as the [yellow]entrypoint[/] in [yellow]serto.json[/].");
            AnsiConsole.MarkupLine("  6. [yellow]serto login --url <control-plane>[/] then [yellow]serto deploy[/]");
            return Task.FromResult(0);
        }

        if (runtime == "shell")
        {
            AnsiConsole.Status().Start($"Creating shell job [green]{projectName}[/]...",
                _ => ScaffoldShell(dir, projectName));

            AnsiConsole.MarkupLine($"[green]Success![/] Shell job [blue]{Markup.Escape(projectName)}[/] created.");
            AnsiConsole.MarkupLine("Next steps:");
            AnsiConsole.MarkupLine($"  1. [yellow]cd {Markup.Escape(projectName)}[/]");
            AnsiConsole.MarkupLine("  2. Edit [yellow]job.sh[/]; declare the command, triggers, and secrets in [yellow]serto.json[/].");
            AnsiConsole.MarkupLine("  3. [yellow]sh job.sh[/]  (run it locally)");
            AnsiConsole.MarkupLine("  4. [yellow]serto scan[/]  (preview what will be provisioned)");
            AnsiConsole.MarkupLine("  5. [yellow]serto login --url <control-plane>[/] then [yellow]serto deploy[/]");
            return Task.FromResult(0);
        }

        var namespaceName = ToNamespace(projectName);
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

    /// <summary>Writes a Python integration scaffold (main.py + serto.json + docs) into <paramref name="dir"/>.</summary>
    public static void ScaffoldPython(string dir, string projectName)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "main.py"), BuildPythonIntegration());
        File.WriteAllText(Path.Combine(dir, ManifestPackaging.ManifestFileName), BuildPythonManifest());
        File.WriteAllText(Path.Combine(dir, "README.md"), BuildPythonReadme(projectName));
        File.WriteAllText(Path.Combine(dir, ".gitignore"), BuildPythonGitignore());
    }

    public static string BuildPythonIntegration() =>
        """
        def handler(ctx):
            ctx.logger.info(f"Hello from {ctx.execution.integration_name}!")

            # Read a secret by name (declare it under requiredSecrets in serto.json):
            # token = ctx.secrets["EXAMPLE_API_TOKEN"]

            # Publish a message other integrations can subscribe to:
            # ctx.publish("example.event", {"hello": "world"})
        """;

    public static string BuildPythonManifest() =>
        """
        {
          "manifestVersion": "1",
          "runtime": "python",
          "integrations": [
            {
              "name": "Sample Integration",
              "slug": "sample-integration",
              "entrypoint": "main.py:handler",
              "triggers": [
                { "type": "scheduled", "cron": "0 * * * *" }
              ],
              "requiredSecrets": []
            }
          ]
        }
        """;

    public static string BuildPythonReadme(string projectName) =>
        $$"""
        # {{projectName}}

        A Serto integration written in Python.

        ## Develop

        - Install the SDK: `pip install serto-sdk` (the import name is `serto`)
        - Edit `main.py` — the `handler(ctx)` function is your integration.
        - Declare triggers, required secrets, and the entrypoint in `serto.json`.
        - `serto scan` — preview the integrations and triggers this package will provision.

        ## The context

        `handler(ctx)` gives you:

        - `ctx.secrets` — configured secret values, by name.
        - `ctx.logger` — `.info(...)`, `.warning(...)`, `.error(...)`, captured into execution history.
        - `ctx.payload` / `ctx.payload_json()` — the raw / parsed body for webhook and message triggers.
        - `ctx.execution` — execution and environment metadata.
        - `ctx.publish(subject, body)` — publish a message other integrations can subscribe to.

        ## Deploy

            serto login --url <control-plane-url>
            serto deploy
        """;

    public static string BuildPythonGitignore() =>
        """
        __pycache__/
        .venv/
        *.pyc
        secrets.json
        """;

    /// <summary>Writes a containerized Go integration scaffold (main.go + go.mod + Dockerfile + serto.json) into <paramref name="dir"/>.</summary>
    public static void ScaffoldGo(string dir, string projectName)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "main.go"), BuildGoIntegration());
        File.WriteAllText(Path.Combine(dir, "go.mod"), BuildGoMod(projectName));
        File.WriteAllText(Path.Combine(dir, "Dockerfile"), BuildGoDockerfile());
        File.WriteAllText(Path.Combine(dir, ManifestPackaging.ManifestFileName), BuildGoManifest(projectName));
        File.WriteAllText(Path.Combine(dir, "README.md"), BuildGoReadme(projectName));
        File.WriteAllText(Path.Combine(dir, ".gitignore"), BuildGoGitignore());
    }

    public static string BuildGoIntegration() =>
        $$"""
        package main

        import serto "{{SertoGoModule}}"

        func main() {
            serto.Run(func(ctx *serto.Context) error {
                ctx.Logger.Infof("Hello from %s!", ctx.Execution.IntegrationName)

                // Read a secret (declare it under requiredSecrets in serto.json):
                // token := ctx.Secrets["EXAMPLE_API_TOKEN"]

                // Publish a message other integrations can subscribe to:
                // return ctx.Publish("example.event", map[string]string{"hello": "world"})

                return nil
            })
        }
        """;

    public static string BuildGoMod(string projectName) =>
        $$"""
        module {{projectName.ToLowerInvariant()}}

        go 1.21

        require {{SertoGoModule}} {{SertoGoVersion}}
        """;

    public static string BuildGoDockerfile() =>
        """
        # Build the integration binary, then ship it in a minimal image.
        FROM golang:1.23-alpine AS build
        WORKDIR /src
        COPY . .
        RUN go mod download && CGO_ENABLED=0 go build -o /app .

        FROM alpine:latest
        COPY --from=build /app /app
        ENTRYPOINT ["/app"]
        """;

    public static string BuildGoManifest(string projectName) =>
        $$"""
        {
          "manifestVersion": "1",
          "runtime": "container",
          "integrations": [
            {
              "name": "Sample Integration",
              "slug": "sample-integration",
              "entrypoint": "your-registry/{{projectName.ToLowerInvariant()}}:latest",
              "triggers": [
                { "type": "scheduled", "cron": "0 * * * *" }
              ],
              "requiredSecrets": []
            }
          ]
        }
        """;

    public static string BuildGoReadme(string projectName) =>
        $$"""
        # {{projectName}}

        A Serto integration written in Go. Go integrations run as a **container image** — the image's
        entrypoint is the compiled binary, which speaks the Serto wire protocol.

        ## Develop

        - Edit `main.go` — the function passed to `serto.Run` is your integration.
        - Declare triggers, required secrets, and the image reference in `serto.json`.
        - `go mod tidy` to fetch the SDK.

        > The Serto Go SDK (`{{SertoGoModule}}`) is not yet published to a module proxy. Until it is,
        > point go.mod at a local checkout:
        > `go mod edit -replace {{SertoGoModule}}=/path/to/serto/sdks/go/serto`

        ## The context

        `serto.Run(func(ctx *serto.Context) error { ... })` gives you:

        - `ctx.Secrets` — configured secret values, by name.
        - `ctx.Logger` — `.Info(...)`, `.Warn(...)`, `.Errorf(...)`, captured into execution history.
        - `ctx.Payload` / `ctx.PayloadJSON(&v)` — the raw / parsed body for webhook and message triggers.
        - `ctx.Execution` — execution and environment metadata.
        - `ctx.Publish(subject, body)` — publish a message other integrations can subscribe to.

        ## Build, push, deploy

            docker build -t <registry>/{{projectName.ToLowerInvariant()}}:latest .
            docker push  <registry>/{{projectName.ToLowerInvariant()}}:latest

        Set that image as the `entrypoint` in `serto.json`, then:

            serto login --url <control-plane-url>
            serto deploy

        The agent pulls and runs your image when the integration is due.
        """;

    public static string BuildGoGitignore() =>
        """
        /app
        *.exe
        secrets.json
        """;

    /// <summary>Writes a Node.js integration scaffold (index.js + package.json + serto.json) into <paramref name="dir"/>.</summary>
    public static void ScaffoldNode(string dir, string projectName)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "index.js"), BuildNodeIntegration());
        File.WriteAllText(Path.Combine(dir, "package.json"), BuildNodePackageJson(projectName));
        File.WriteAllText(Path.Combine(dir, ManifestPackaging.ManifestFileName), BuildNodeManifest());
        File.WriteAllText(Path.Combine(dir, "README.md"), BuildNodeReadme(projectName));
        File.WriteAllText(Path.Combine(dir, ".gitignore"), BuildNodeGitignore());
    }

    public static string BuildNodeIntegration() =>
        """
        module.exports.handler = async (ctx) => {
          ctx.logger.info(`Hello from ${ctx.execution.integrationName}!`);

          // Read a secret (declare it under requiredSecrets in serto.json):
          // const token = ctx.secrets.EXAMPLE_API_TOKEN;

          // Publish a message other integrations can subscribe to:
          // await ctx.publish('example.event', { hello: 'world' });
        };
        """;

    public static string BuildNodePackageJson(string projectName) =>
        $$"""
        {
          "name": "{{projectName.ToLowerInvariant()}}",
          "version": "0.1.0",
          "private": true,
          "dependencies": {
            "{{SertoNodePackage}}": "{{SertoNodeVersion}}"
          }
        }
        """;

    public static string BuildNodeManifest() =>
        """
        {
          "manifestVersion": "1",
          "runtime": "node",
          "integrations": [
            {
              "name": "Sample Integration",
              "slug": "sample-integration",
              "entrypoint": "index.js#handler",
              "triggers": [
                { "type": "scheduled", "cron": "0 * * * *" }
              ],
              "requiredSecrets": []
            }
          ]
        }
        """;

    public static string BuildNodeReadme(string projectName) =>
        $$"""
        # {{projectName}}

        A Serto integration written in Node.js.

        ## Develop

        - Install the SDK: `npm install`
        - Edit `index.js` — the exported `handler(ctx)` is your integration.
        - Declare triggers, required secrets, and the entrypoint in `serto.json`.
        - `serto scan` — preview the integrations and triggers this package will provision.

        ## The context

        `handler(ctx)` gives you:

        - `ctx.secrets` — configured secret values, by name.
        - `ctx.logger` — `.info(...)`, `.warn(...)`, `.error(...)`, captured into execution history.
        - `ctx.payload` / `ctx.payloadJson()` — the raw / parsed body for webhook and message triggers.
        - `ctx.execution` — execution and environment metadata.
        - `ctx.publish(subject, body)` — publish a message other integrations can subscribe to.

        Dependency-free integrations run as a subprocess. If you need npm dependencies at runtime, ship a
        container image (`runtime: "container"`) instead.

        ## Deploy

            serto login --url <control-plane-url>
            serto deploy
        """;

    public static string BuildNodeGitignore() =>
        """
        node_modules/
        secrets.json
        """;

    /// <summary>Writes a shell-job scaffold (job.sh + serto.json) into <paramref name="dir"/>.</summary>
    public static void ScaffoldShell(string dir, string projectName)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "job.sh"), BuildShellScript());
        File.WriteAllText(Path.Combine(dir, ManifestPackaging.ManifestFileName), BuildShellManifest());
        File.WriteAllText(Path.Combine(dir, "README.md"), BuildShellReadme(projectName));
        File.WriteAllText(Path.Combine(dir, ".gitignore"), BuildShellGitignore());
    }

    public static string BuildShellScript() =>
        """
        #!/bin/sh
        set -e

        echo "Running $SERTO_INTEGRATION_NAME in $SERTO_ENVIRONMENT"

        # Secrets arrive as environment variables, by name. Declare them under
        # requiredSecrets in serto.json, then use them here:
        #   echo "Using API key: $EXAMPLE_API_TOKEN"

        # Do the work — run a query, call a CLI, move a file, sync data, ...
        echo "Done."
        """;

    public static string BuildShellManifest() =>
        """
        {
          "manifestVersion": "1",
          "runtime": "shell",
          "integrations": [
            {
              "name": "Sample Job",
              "slug": "sample-job",
              "entrypoint": "sh job.sh",
              "triggers": [
                { "type": "scheduled", "cron": "0 * * * *" }
              ],
              "requiredSecrets": []
            }
          ]
        }
        """;

    public static string BuildShellReadme(string projectName) =>
        $$"""
        # {{projectName}}

        A Serto shell job — a raw script with scheduling, secrets, logs, retries, and alerts around it.
        Bring a script you already run under cron / Control-M / EBS; no rewrite required.

        ## Develop

        - Edit `job.sh` — your script. Run it locally with `sh job.sh`.
        - Set the command, triggers, and required secrets in `serto.json` (the `entrypoint` is a command
          line — `sh job.sh`, `sqlplus -s "$DB_USER/$DB_PW@orcl" @close.sql`, any executable).
        - `serto scan` — preview the job and triggers this package will provision.

        ## What your script gets

        - **Secrets** as environment variables, by name (list them under `requiredSecrets`).
        - `SERTO_EXECUTION_ID`, `SERTO_INTEGRATION_NAME`, `SERTO_ENVIRONMENT`, `SERTO_SCHEDULED_AT`,
          `SERTO_TRIGGER_TYPE`, and (for webhook/message triggers) `SERTO_PAYLOAD` / `SERTO_MESSAGE_SUBJECT`.

        All stdout and stderr is captured as logs; a non-zero exit code marks the run failed.

        ## Deploy

            serto login --url <control-plane-url>
            serto deploy

        The agent runs the command through its shell, so the agent host must have whatever your script needs
        (a shell, `sqlplus`, …). For heavy dependencies, package it as a container image instead.
        """;

    public static string BuildShellGitignore() =>
        """
        secrets.json
        """;

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
