namespace IntegrationPlatform.Sdk;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class IntegrationAttribute(string name, string slug) : Attribute
{
    public string Name { get; } = name;
    public string Slug { get; } = slug;
    public string? Description { get; set; }
    public int? TimeoutSeconds { get; set; }
    public int? RetryMaxAttempts { get; set; }
    public int? RetryBackoffSeconds { get; set; }
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
