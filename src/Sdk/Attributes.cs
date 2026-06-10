namespace Serto.Sdk;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class IntegrationAttribute(string name, string slug) : Attribute
{
    public string Name { get; } = name;
    public string Slug { get; } = slug;
    public string? Description { get; set; }
    public int TimeoutSeconds { get; set; }
    public int RetryMaxAttempts { get; set; }
    public int RetryBackoffSeconds { get; set; }
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ScheduledIntegrationAttribute(string name, string slug, string cronExpression) 
    : IntegrationAttribute(name, slug)
{
    public string CronExpression { get; } = cronExpression;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class WebhookIntegrationAttribute(string name, string slug)
    : IntegrationAttribute(name, slug)
{
}

// Declares the agent capabilities this integration requires to run. The control plane will only
// route the integration's work to an agent that offers all of these tags (e.g. a host wired to
// specific hardware, behind a particular network, or with a licensed driver). Omit it to run on
// any agent in the integration's environment. Operators can override the required tags in the
// control plane; this attribute is the code-declared default.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresAgentCapabilitiesAttribute(params string[] tags) : Attribute
{
    public string[] Tags { get; } = tags;
}
