using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Invitations;

public class InvitationRepository(AppDbContext db)
    : IInvitationRepository, IInvitationReadRepository, IInvitationRevocationRepository, IInvitationResendRepository
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

    public async Task<bool> RevokePendingAsync(Guid tenantId, Guid invitationId, CancellationToken ct = default)
    {
        var invitation = await PendingInvitationAsync(tenantId, invitationId, ct);
        if (invitation is null)
            return false;

        invitation.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        invitation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Invitation?> ResendPendingAsync(Guid tenantId, Guid invitationId, string token, DateTime expiresAt, CancellationToken ct = default)
    {
        var invitation = await PendingInvitationAsync(tenantId, invitationId, ct);
        if (invitation is null)
            return null;

        invitation.Token = token;
        invitation.ExpiresAt = expiresAt;
        invitation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return invitation;
    }

    private Task<Invitation?> PendingInvitationAsync(Guid tenantId, Guid invitationId, CancellationToken ct) =>
        db.Invitations.FirstOrDefaultAsync(i =>
            i.TenantId == tenantId &&
            i.Id == invitationId &&
            i.AcceptedAt == null &&
            i.ExpiresAt >= DateTime.UtcNow, ct);
}
