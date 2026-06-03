using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public interface IWorkItemRepository
{
    Task<WorkItem?> GetByIdAsync(Guid tenantId, Guid workItemId, CancellationToken ct = default);
    Task<WorkItem> CreateAsync(WorkItem workItem, CancellationToken ct = default);
    Task UpdateAsync(WorkItem workItem, CancellationToken ct = default);
}

public class WorkItemRepository(AppDbContext db) : IWorkItemRepository
{
    public Task<WorkItem?> GetByIdAsync(Guid tenantId, Guid workItemId, CancellationToken ct = default)
    {
        return db.WorkItems
            .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.Id == workItemId, ct);
    }

    public async Task<WorkItem> CreateAsync(WorkItem workItem, CancellationToken ct = default)
    {
        db.WorkItems.Add(workItem);
        await db.SaveChangesAsync(ct);
        return workItem;
    }

    public async Task UpdateAsync(WorkItem workItem, CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
    }
}
