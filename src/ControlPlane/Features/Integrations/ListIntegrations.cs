using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Features.Integrations;

public record ListIntegrationsCommand(Guid TenantId, string? Environment) : ICommand<ListIntegrationsResult>;

public record ListIntegrationsResult(IReadOnlyList<ListIntegrationItem> Integrations);

public record ListIntegrationItem(
    Guid Id,
    string Name,
    string Slug,
    string Environment,
    string Status,
    string TriggerType,
    string? CronExpression,
    string ClassName,
    int? TimeoutSeconds,
    Guid? PackageId,
    ExecutionSummary? LastExecution,
    string? WebhookUrl = null);

public class ListIntegrationsHandler(
    IIntegrationReadRepository repository,
    IExecutionHistoryRepository executionHistory)
    : ICommandHandler<ListIntegrationsCommand, ListIntegrationsResult>
{
    public async Task<ListIntegrationsResult> HandleAsync(ListIntegrationsCommand command, CancellationToken ct = default)
    {
        var integrations = await repository.ListAsync(command.TenantId, command.Environment, ct);
        var latestExecutions = await executionHistory.GetLatestForIntegrationsAsync(
            command.TenantId,
            integrations.Select(i => i.Id).ToList(),
            ct);

        // Look up once for webhook URL construction
        var tenantSlug = integrations.Any(i => i.TriggerType == TriggerType.Webhook)
            ? await repository.GetTenantSlugAsync(command.TenantId, ct)
            : null;

        var results = integrations
            .Select(i =>
            {
                latestExecutions.TryGetValue(i.Id, out var lastExecution);
                var webhookUrl = i.TriggerType == TriggerType.Webhook && tenantSlug is not null
                    ? $"/webhooks/{tenantSlug}/{i.Slug}"
                    : null;

                return new ListIntegrationItem(
                    i.Id, i.Name, i.Slug, i.Environment,
                    i.Status.ToString(), i.TriggerType.ToString(),
                    i.CronExpression, i.ClassName, i.TimeoutSeconds, i.PackageId,
                    lastExecution is null ? null : ListIntegrationExecutionsHandler.ToSummary(lastExecution),
                    webhookUrl);
            })
            .ToList();

        return new ListIntegrationsResult(results);
    }
}
