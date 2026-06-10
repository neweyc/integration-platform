using RuntimeAgent;
using RuntimeAgent.Agent;
using RuntimeAgent.Execution;
using Shared.Domain;

var builder = Host.CreateApplicationBuilder(args);

var options = builder.Configuration
    .GetSection("Agent")
    .Get<AgentOptions>() ?? throw new InvalidOperationException("Agent configuration is missing.");

// Environments are canonical (lowercase) in the control plane. Normalize here so an agent configured
// as "Production" matches its token's stored environment and loads secrets correctly.
options.Environment = EnvironmentKey.Normalize(options.Environment);

builder.Services.AddSingleton(options);

// Extend the host shutdown timeout to cover the full drain window plus a small buffer
builder.Services.Configure<HostOptions>(o =>
    o.ShutdownTimeout = TimeSpan.FromSeconds(options.ShutdownDrainSeconds + 10));

// HTTP client for the control plane — attaches the agent token (and offered capabilities) to every request
builder.Services.AddHttpClient<IControlPlaneClient, ControlPlaneClient>(client =>
{
    client.BaseAddress = new Uri(options.ControlPlaneUrl);
    client.DefaultRequestHeaders.Add("X-Agent-Token", options.AgentToken);

    // Advertise the agent's capabilities so the control plane only routes work this agent can run.
    var tags = options.Tags
        .Where(tag => !string.IsNullOrWhiteSpace(tag))
        .Select(tag => tag.Trim())
        .ToArray();
    if (tags.Length > 0)
        client.DefaultRequestHeaders.Add("X-Agent-Capabilities", string.Join(",", tags));
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    // Proactively retire idle connections before the server's keep-alive timeout closes them.
    // Without this, the agent can reuse a connection the control plane has already dropped during
    // the gap between polling and reporting a result, surfacing as "the response ended prematurely".
    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(options.ControlPlaneConnectionIdleTimeoutSeconds),
    // Recycle connections periodically so DNS/load-balancer changes are picked up.
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
});

// Named HTTP client available inside integrations via IIntegrationContext.Http
builder.Services.AddHttpClient("integration");

builder.Services.AddSingleton<IntegrationLoader>();
builder.Services.AddScoped<IntegrationExecutor>();
builder.Services.AddScoped<PackageSyncer>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
