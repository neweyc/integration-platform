using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Invitations;

public class InvitationRepository(AppDbContext db) : IInvitationRepository
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
}
