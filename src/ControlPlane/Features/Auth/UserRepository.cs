using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Features.Auth;

public class UserRepository(AppDbContext db)
    : IUserRepository, IUserReadRepository, IUserListRepository
{
    public Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken ct = default) =>
        db.Users.AnyAsync(u => u.TenantId == tenantId && u.Email == email, ct);

    public Task<bool> HasAnyAdminAsync(Guid tenantId, CancellationToken ct = default) =>
        db.Users.AnyAsync(u => u.TenantId == tenantId && u.Role == UserRole.Admin, ct);

    public async Task<User> CreateAsync(User user, CancellationToken ct = default)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public async Task<IReadOnlyList<User>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.Users
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.Email)
            .ToListAsync(ct);
}
