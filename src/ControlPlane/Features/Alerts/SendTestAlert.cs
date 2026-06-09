using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Features.Alerts;

// Sends a sample failure alert through whatever channels are currently configured, so an operator can
// confirm SMTP/ZeptoMail/webhook delivery works. Pass an integration id to test that integration's
// effective configuration (including any override); omit it to test the tenant defaults.
public record SendTestAlertCommand(Guid TenantId, Guid? IntegrationId)
    : ICommand<AlertSendOutcome>, IAuditableCommand
{
    public AuditDescriptor? Describe(object? result) =>
        new(AuditAction.TestAlertSent, "AlertSettings", TenantId.ToString(), "Sent a test alert");
}

public class SendTestAlertHandler(IAlertNotifier notifier, IAlertSettingsReadRepository repository)
    : ICommandHandler<SendTestAlertCommand, AlertSendOutcome>
{
    public async Task<AlertSendOutcome> HandleAsync(SendTestAlertCommand command, CancellationToken ct = default)
    {
        // A per-integration test must target a real integration in this tenant, not an arbitrary id.
        if (command.IntegrationId is { } integrationId
            && !await repository.IntegrationExistsAsync(command.TenantId, integrationId, ct))
            throw new NotFoundException($"Integration '{integrationId}' not found.");

        var sample = new FailedExecutionAlert(
            TenantId: command.TenantId,
            IntegrationId: command.IntegrationId ?? Guid.Empty,
            IntegrationName: "Test integration",
            Environment: "test",
            ExecutionId: Guid.NewGuid(),
            Status: ExecutionStatus.Failed,
            ErrorMessage: "This is a test alert from Serto. If you received it, alerting is configured correctly.",
            AttemptNumber: 1,
            PackageName: null,
            PackageVersion: null,
            FailedAt: DateTime.UtcNow);

        var outcome = await notifier.SendAsync(sample, ct);

        if (!outcome.AnyAttempted)
            throw new ValidationException(
                "No alert channel is configured to send to. Enable email or webhook alerts first, then save.");

        return outcome;
    }
}
