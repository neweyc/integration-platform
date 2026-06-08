namespace RuntimeAgent.Agent;

public class AgentOptions
{
    // Base URL of the control plane API, e.g. https://mycontrolplane.com
    public string ControlPlaneUrl { get; set; } = "";

    // Agent token in the format agt_xxx — created in the control plane UI
    public string AgentToken { get; set; } = "";

    // The environment this agent is responsible for, e.g. "production"
    public string Environment { get; set; } = "";

    // Directory to scan for integration assemblies (.dll files)
    public string IntegrationsPath { get; set; } = "";

    // How often to poll for due integrations, in seconds
    public int PollIntervalSeconds { get; set; } = 30;

    // Maximum number of integrations that can execute concurrently
    public int MaxConcurrentExecutions { get; set; } = 5;

    // Seconds to wait for in-flight executions to finish on shutdown before cancelling them
    public int ShutdownDrainSeconds { get; set; } = 30;

    // Directory where packages downloaded from the control plane are extracted
    public string PackagesPath { get; set; } = "./packages";

    // How often to check for new or updated packages, in seconds
    public int PackageSyncIntervalSeconds { get; set; } = 300;

    // How long a pooled HTTP connection to the control plane may sit idle before the agent
    // proactively closes it. Kept well below typical server keep-alive timeouts so the agent
    // never reuses a connection the server has already dropped — the cause of intermittent
    // "the response ended prematurely" errors when reporting logs or completing executions.
    public int ControlPlaneConnectionIdleTimeoutSeconds { get; set; } = 15;
}
