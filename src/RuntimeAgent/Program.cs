using RuntimeAgent;
using RuntimeAgent.Agent;
using RuntimeAgent.Execution;

var builder = Host.CreateApplicationBuilder(args);

var options = builder.Configuration
    .GetSection("Agent")
    .Get<AgentOptions>() ?? throw new InvalidOperationException("Agent configuration is missing.");

builder.Services.AddSingleton(options);

// Extend the host shutdown timeout to cover the full drain window plus a small buffer
builder.Services.Configure<HostOptions>(o =>
    o.ShutdownTimeout = TimeSpan.FromSeconds(options.ShutdownDrainSeconds + 10));

// HTTP client for the control plane — attaches the agent token to every request
builder.Services.AddHttpClient<IControlPlaneClient, ControlPlaneClient>(client =>
{
    client.BaseAddress = new Uri(options.ControlPlaneUrl);
    client.DefaultRequestHeaders.Add("X-Agent-Token", options.AgentToken);
});

// Named HTTP client available inside integrations via IIntegrationContext.Http
builder.Services.AddHttpClient("integration");

builder.Services.AddSingleton<IntegrationLoader>();
builder.Services.AddScoped<IntegrationExecutor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
