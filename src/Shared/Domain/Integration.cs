namespace Shared.Domain;

public class Integration : Entity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Environment { get; set; } = string.Empty;
    public IntegrationStatus Status { get; set; } = IntegrationStatus.Enabled;

    // Fully qualified class name that implements IIntegration (e.g. "MyCompany.Integrations.SyncOrdersIntegration"),
    // or, for non-.NET runtimes, the runtime-specific entrypoint locator (e.g. "main.py:handler"). The
    // runtime agent uses this to locate and run the integration.
    public string ClassName { get; set; } = string.Empty;

    // Execution runtime: "dotnet" (default, in-process .NET) or another runtime ("python", "node", …) the
    // agent runs out-of-process. Denormalized from the integration's package so dispatch can pick the right
    // runner without a join. See docs/multi-language-runtimes.md.
    public string Runtime { get; set; } = "dotnet";

    // Maximum seconds an execution may run before being cancelled. Null means no timeout.
    public int? TimeoutSeconds { get; set; }

    // Number of retry attempts after the initial attempt. Zero disables retries.
    public int RetryMaxAttempts { get; set; }

    // Delay before the next retry attempt. Null means retry immediately.
    public int? RetryBackoffSeconds { get; set; }

    // Pinned package. Null means the agent resolves from its local IntegrationsPath in dev mode.
    public Guid? PackageId { get; set; }

    // Agent capabilities required to run this integration. Work is only routed to an agent whose
    // offered tags are a superset of these. Empty = runnable on any agent in the environment.
    // RequiredTags is the active (operator-overridable) value; DeclaredRequiredTags is what the
    // code last declared. They diverge when an operator overrides the tags in the control plane —
    // that divergence is preserved across package redeploys and reported as drift, mirroring how
    // trigger cron/enabled overrides work.
    public string[] RequiredTags { get; set; } = [];
    public string[] DeclaredRequiredTags { get; set; } = [];

    // Set when an "unroutable" alert has been sent for this integration (no live agent offers its
    // required tags), and cleared when it becomes routable again. Dedups the alert so it fires once
    // per transition into the unroutable state rather than every monitor sweep.
    public DateTime? UnroutableAlertedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public List<IntegrationTrigger> Triggers { get; set; } = [];
}

public enum IntegrationStatus
{
    Enabled,
    Disabled
}

public enum TriggerType
{
    Scheduled,
    Webhook,
    Manual,
    Queue,
    File
}
