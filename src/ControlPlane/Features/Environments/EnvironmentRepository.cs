using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Environment = Shared.Domain.Environment;

namespace ControlPlane.Features.Environments;

/// <summary>
/// How many live, environment-scoped records reference a given environment. Used to decide whether
/// an environment can be deleted and to give the operator a specific reason when it cannot.
/// Historical/transient records (executions, work items, heartbeats) are deliberately excluded —
/// they should never pin an environment that no longer has any live configuration.
/// </summary>
public record EnvironmentUsage(int Secrets, int Integrations, int AgentTokens, int Workflows)
{
    public bool InUse => Secrets > 0 || Integrations > 0 || AgentTokens > 0 || Workflows > 0;
}

public interface IEnvironmentReadRepository
{
    Task<IReadOnlyList<Environment>> ListAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid tenantId, string name, CancellationToken ct = default);

    /// <summary>
    /// The tenant's default environment name (the one flagged default, else the first by sort order),
    /// or null if the tenant has no environments. Used as the target for package auto-provisioning.
    /// </summary>
    Task<string?> GetDefaultNameAsync(Guid tenantId, CancellationToken ct = default);
}

public interface IEnvironmentWriteRepository
{
    Task<Environment?> FindAsync(Guid tenantId, string name, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid tenantId, string name, CancellationToken ct = default);
    Task AddAsync(Environment environment, CancellationToken ct = default);
    Task<IReadOnlyList<Environment>> ListTrackedAsync(Guid tenantId, CancellationToken ct = default);
    Task<EnvironmentUsage> GetUsageAsync(Guid tenantId, string name, CancellationToken ct = default);
    void Remove(Environment environment);
    Task SaveAsync(CancellationToken ct = default);
}

public class EnvironmentRepository(AppDbContext db)
    : IEnvironmentReadRepository, IEnvironmentWriteRepository
{
    public async Task<IReadOnlyList<Environment>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.Environments
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.Name)
            .ToListAsync(ct);

    public Task<bool> ExistsAsync(Guid tenantId, string name, CancellationToken ct = default) =>
        db.Environments.AnyAsync(e => e.TenantId == tenantId && e.Name == name, ct);

    public async Task<string?> GetDefaultNameAsync(Guid tenantId, CancellationToken ct = default)
    {
        var environments = await db.Environments
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.IsDefault)   // the default, if any, sorts first
            .ThenBy(e => e.SortOrder)
            .ThenBy(e => e.Name)
            .Select(e => e.Name)
            .ToListAsync(ct);

        return environments.Count > 0 ? environments[0] : null;
    }

    public Task<Environment?> FindAsync(Guid tenantId, string name, CancellationToken ct = default) =>
        db.Environments.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Name == name, ct);

    public async Task<IReadOnlyList<Environment>> ListTrackedAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.Environments.Where(e => e.TenantId == tenantId).ToListAsync(ct);

    public async Task AddAsync(Environment environment, CancellationToken ct = default)
    {
        db.Environments.Add(environment);
        await Task.CompletedTask;
    }

    public async Task<EnvironmentUsage> GetUsageAsync(Guid tenantId, string name, CancellationToken ct = default)
    {
        var secrets = await db.Secrets.CountAsync(s => s.TenantId == tenantId && s.Environment == name, ct);
        var integrations = await db.Integrations.CountAsync(i => i.TenantId == tenantId && i.Environment == name, ct);
        var agentTokens = await db.AgentTokens.CountAsync(t => t.TenantId == tenantId && t.Environment == name, ct);
        var workflows = await db.WorkflowDefinitions.CountAsync(w => w.TenantId == tenantId && w.Environment == name, ct);
        return new EnvironmentUsage(secrets, integrations, agentTokens, workflows);
    }

    public void Remove(Environment environment) => db.Environments.Remove(environment);

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
