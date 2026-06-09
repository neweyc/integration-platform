using ControlPlane.Infrastructure;
using Environment = Shared.Domain.Environment;

namespace ControlPlane.Tests.IntegrationTests;

/// <summary>
/// Seeds environment-registry rows for tests that build a tenant directly through the DbContext
/// (bypassing the normal tenant-creation path that auto-seeds a default environment). Without these
/// rows, inserting an integration/secret/agent token/workflow would violate the environment foreign key.
/// </summary>
internal static class TestEnvironments
{
    public static void Seed(AppDbContext db, Guid tenantId, params string[] names)
    {
        if (names.Length == 0)
            names = ["production"];

        var order = 0;
        foreach (var name in names)
        {
            db.Environments.Add(new Environment
            {
                TenantId = tenantId,
                Name = name,
                DisplayName = name,
                SortOrder = order,
                IsDefault = order == 0
            });
            order++;
        }
    }
}
