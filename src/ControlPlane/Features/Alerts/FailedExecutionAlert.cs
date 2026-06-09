using Shared.Domain;

namespace ControlPlane.Features.Alerts;

// A terminal integration failure worth alerting on. Built at the point the execution is finalized and
// handed to the dispatch queue so the agent's completion call is never blocked on sending the alert.
public record FailedExecutionAlert(
    Guid TenantId,
    Guid IntegrationId,
    string IntegrationName,
    string Environment,
    Guid ExecutionId,
    ExecutionStatus Status,
    string? ErrorMessage,
    int AttemptNumber,
    string? PackageName,
    string? PackageVersion,
    DateTime FailedAt);
