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
}
