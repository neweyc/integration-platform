using Spectre.Console.Cli;
using Cli;
using Cli.Commands;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("serto");
    // Enables `serto --version`, printing the build-stamped CLI version.
    config.SetApplicationVersion(CliVersion.Current);
    config.AddCommand<InitCommand>("init")
        .WithDescription("Scaffold a new integration project");

    config.AddCommand<DeployCommand>("deploy")
        .WithDescription("Deploy the current project to the Control Plane");

    config.AddCommand<LoginCommand>("login")
        .WithDescription("Save an API token for a control plane so deploys don't prompt for it");

    config.AddCommand<LogoutCommand>("logout")
        .WithDescription("Remove a saved API token");

    config.AddCommand<ScanCommand>("scan")
        .WithDescription("Preview integrations and triggers discovered from the current project");

    config.AddCommand<PackageCommand>("package")
        .WithDescription("Build, validate, and archive the current integration project");

    config.AddCommand<TestCommand>("test")
        .WithDescription("Run an integration locally for testing");

    config.AddBranch("webhook", webhook =>
    {
        webhook.SetDescription("Webhook development tools");
        webhook.AddCommand<WebhookReplayCommand>("replay")
            .WithDescription("Sign and replay a sample webhook payload");
    });

    config.AddCommand<DevCommand>("dev")
        .WithDescription("Watch for file changes and run tests automatically");
});

return await app.RunAsync(args);
