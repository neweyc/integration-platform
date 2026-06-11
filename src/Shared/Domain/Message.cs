namespace Shared.Domain;

/// <summary>
/// A published message envelope — the canonical record of what was published. This is distinct from
/// how a message is delivered: each subscriber receives the <see cref="Body"/> raw on its work item,
/// with the remaining fields surfaced as trigger metadata. The envelope is persisted for
/// observability, lineage, and replay (storage format is not the same as delivery format).
/// </summary>
public class Message : Entity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public string Environment { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;

    // The serialized message body (JSON), delivered verbatim to subscribers as WorkItem.Payload.
    public string? Body { get; set; }

    // The execution that published this message, for end-to-end lineage. Null if published outside an
    // execution context.
    public Guid? SourceExecutionId { get; set; }

    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
}
