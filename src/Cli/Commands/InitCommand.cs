using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Cli.Commands;

public sealed class InitCommand : AsyncCommand<InitCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[ProjectName]")]
        [Description("The name of the integration project to create")]
        public string? ProjectName { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        var projectName = settings.ProjectName ?? AnsiConsole.Ask<string>("What is the [green]name[/] of your integration project?");
        
        var dir = Path.Combine(Directory.GetCurrentDirectory(), projectName);
        if (Directory.Exists(dir))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Directory [yellow]{projectName}[/] already exists.");
            return 1;
        }

        AnsiConsole.Status()
            .Start($"Creating integration project [green]{projectName}[/]...", ctx =>
            {
                Directory.CreateDirectory(dir);
                
                // Create .csproj
                var csproj = $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Serto.Sdk" Version="1.0.0" />
    <PackageReference Include="Serto.Connectors" Version="1.0.0" />
  </ItemGroup>
</Project>
""";
                File.WriteAllText(Path.Combine(dir, $"{projectName}.csproj"), csproj);

                // Create a sample integration
                var sampleClass = $$"""
using Serto.Sdk;
using Serto.Connectors.Http;
using Microsoft.Extensions.Logging;

namespace {{projectName}};

[ScheduledIntegration("Sample Integration", "sample-integration", "0 * * * *")]
public class MyIntegration : IIntegration
{
    public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        context.Logger.LogInformation("Hello from {IntegrationName}!", context.Execution.IntegrationName);
        
        // Example: Call an API
        // var client = context.HttpConnector("https://api.example.com");
        // var data = await client.GetJsonAsync<dynamic>("/data", ct);
        
        context.Logger.LogInformation("Integration completed successfully.");
    }
}
""";
                File.WriteAllText(Path.Combine(dir, "MyIntegration.cs"), sampleClass);

                // Create a .gitignore
                var gitignore = """
bin/
obj/
*.user
""";
                File.WriteAllText(Path.Combine(dir, ".gitignore"), gitignore);
            });

        AnsiConsole.MarkupLine($"[green]Success![/] Integration project [blue]{projectName}[/] created.");
        AnsiConsole.MarkupLine("Next steps:");
        AnsiConsole.MarkupLine($"  1. [yellow]cd {projectName}[/]");
        AnsiConsole.MarkupLine("  2. [yellow]dotnet build[/]");
        AnsiConsole.MarkupLine("  3. [yellow]serto deploy[/] (coming soon)");

        return 0;
    }
}
