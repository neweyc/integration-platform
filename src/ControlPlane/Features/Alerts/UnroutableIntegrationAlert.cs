namespace ControlPlane.Features.Alerts;

// Raised when an enabled integration's required agent capabilities are offered by no live agent in its
// environment, so its work can't be claimed by anyone. Delivered through the same channels as failure
// alerts, deduped by the monitor (sent once per transition into the unroutable state).
public record UnroutableIntegrationAlert(
    Guid TenantId,
    Guid IntegrationId,
    string IntegrationName,
    string Slug,
    string Environment,
    IReadOnlyList<string> RequiredTags,
    DateTime DetectedAt);
