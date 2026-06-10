using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Onboarding;

// The first-run milestones a tenant has reached, used to drive the getting-started checklist.
public record OnboardingProgress(bool HasAgentToken, bool HasIntegration, bool HasSuccessfulExecution);

public interface IOnboardingRepository
{
    Task<OnboardingProgress> GetProgressAsync(Guid tenantId, CancellationToken ct = default);
}

public class OnboardingRepository(AppDbContext db) : IOnboardingRepository
{
    public async Task<OnboardingProgress> GetProgressAsync(Guid tenantId, CancellationToken ct = default)
    {
        var hasAgentToken = await db.AgentTokens.AnyAsync(t => t.TenantId == tenantId, ct);
        var hasIntegration = await db.Integrations.AnyAsync(i => i.TenantId == tenantId, ct);
        var hasSuccessfulExecution = await db.ExecutionRecords
            .AnyAsync(e => e.TenantId == tenantId && e.Status == ExecutionStatus.Succeeded, ct);

        return new OnboardingProgress(hasAgentToken, hasIntegration, hasSuccessfulExecution);
    }
}
