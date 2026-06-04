using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Invitations;

public class InvitationRepository(AppDbContext db) : IInvitationRepository, IInvitationReadRepository
{
    public async Task<Invitation> CreateAsync(Invitation invitation, CancellationToken ct = default)
    {
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);
        return invitation;
    }

    public Task<Invitation?> GetByTokenAsync(string token, CancellationToken ct = default) =>
        db.Invitations.FirstOrDefaultAsync(i => i.Token == token, ct);

    public async Task UpdateAsync(Invitation invitation, CancellationToken ct = default)
    {
        db.Invitations.Update(invitation);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Invitation>> ListPendingByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.Invitations
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.AcceptedAt == null && i.ExpiresAt >= DateTime.UtcNow)
            .OrderBy(i => i.Email)
            .ToListAsync(ct);
}
