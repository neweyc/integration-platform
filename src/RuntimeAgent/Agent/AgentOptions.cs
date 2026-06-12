namespace RuntimeAgent.Agent;

public class AgentOptions
{
    // Base URL of the control plane API, e.g. https://mycontrolplane.com
    public string ControlPlaneUrl { get; set; } = "";

    // Agent token in the format agt_xxx — created in the control plane UI
    public string AgentToken { get; set; } = "";

    // The environment this agent is responsible for, e.g. "production"
    public string Environment { get; set; } = "";

    // Capabilities this agent offers, e.g. ["hardware-signal", "site-floor-1"]. The control plane only
    // routes an integration to this agent when its required tags are all present here. Empty = the agent
    // can run any integration in its environment.
    public string[] Tags { get; set; } = [];

    // Address of the secret vault on the customer's network, e.g. https://vault.internal:8200. Required
    // only when the control plane runs the external-vault backend (it then hands the agent references
    // instead of values, and the agent resolves them here). Leave empty for the embedded backend.
    public string? VaultAddress { get; set; }

    // Token the agent presents to the vault, if the vault requires authentication.
    public string? VaultToken { get; set; }

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

    // How to launch out-of-process language runtimes, keyed by runtime name ("python", "node", "go", …).
    // The command is host-specific (interpreter paths vary), so it's configuration rather than a built-in
    // default. The entrypoint is NOT passed here — it travels to the integration over the wire protocol,
    // so the command only needs to start that runtime's Serto harness. A relative command is resolved
    // against the package directory (e.g. a Go binary shipped inside the package); otherwise it resolves
    // from PATH. Empty = this agent runs only in-process .NET integrations.
    public Dictionary<string, RuntimeLaunchOptions> Runtimes { get; set; } = new();

    // How to run container-runtime integrations. The image reference is the integration's entrypoint; the
    // engine and base run args are configured here. Secrets ride in the stdin invocation, not env vars, so
    // nothing sensitive lands in `docker inspect` or the process table.
    public ContainerOptions Container { get; set; } = new();

    // How to run "shell" runtime integrations — raw commands and scripts (no SDK). The integration's
    // entrypoint is a command line run through this shell (default `/bin/sh -c "<command>"`). Inputs reach
    // the script as environment variables (secrets by their own name + SERTO_* metadata); all stdout/stderr
    // is captured as logs; the exit code decides success.
    public ShellOptions Shell { get; set; } = new();
}

public class RuntimeLaunchOptions
{
    public string Command { get; set; } = "";
    public string[] Args { get; set; } = [];
}

public class ContainerOptions
{
    public string Engine { get; set; } = "docker";
    public string[] RunArgs { get; set; } = ["run", "--rm", "-i"];
}

public class ShellOptions
{
    public string Executable { get; set; } = "/bin/sh";
    public string[] Args { get; set; } = ["-c"];
}
