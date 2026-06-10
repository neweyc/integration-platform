using ControlPlane.Infrastructure;

namespace ControlPlane.Features.Billing;

public record GetBillingStatusCommand(Guid TenantId) : ICommand<BillingStatusResult>;

public record BillingStatusResult(
    string Plan,
    string? SubscriptionStatus,
    int ExecutionsUsed,
    int ExecutionLimit,
    bool BillingEnabled,
    bool HasBillingAccount);

public class GetBillingStatusHandler(
    IBillingRepository repository,
    IQuotaService quotaService,
    StripeOptions options)
    : ICommandHandler<GetBillingStatusCommand, BillingStatusResult>
{
    public async Task<BillingStatusResult> HandleAsync(GetBillingStatusCommand command, CancellationToken ct = default)
    {
        var tenant = await repository.GetByIdAsync(command.TenantId, ct)
            ?? throw new NotFoundException("Tenant not found.");

        var used = await quotaService.GetCurrentMonthlyExecutionCountAsync(command.TenantId, ct);

        return new BillingStatusResult(
            tenant.Plan.ToString(),
            tenant.SubscriptionStatus,
            used,
            tenant.MaxExecutionsPerMonth,
            options.IsConfigured,
            !string.IsNullOrWhiteSpace(tenant.StripeCustomerId));
    }
}
