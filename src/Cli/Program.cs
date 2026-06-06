using Spectre.Console.Cli;
using Cli.Commands;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("ip");
    config.AddCommand<InitCommand>("init")
        .WithDescription("Scaffold a new integration project");

    config.AddCommand<DeployCommand>("deploy")
        .WithDescription("Deploy the current project to the Control Plane");

    config.AddCommand<ScanCommand>("scan")
        .WithDescription("Preview integrations and triggers discovered from the current project");

    config.AddCommand<TestCommand>("test")
        .WithDescription("Run an integration locally for testing");

    config.AddCommand<DevCommand>("dev")
        .WithDescription("Watch for file changes and run tests automatically");
});

return await app.RunAsync(args);
