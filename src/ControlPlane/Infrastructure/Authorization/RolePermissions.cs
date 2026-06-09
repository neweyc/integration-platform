using Shared.Domain;

namespace ControlPlane.Infrastructure.Authorization;

/// <summary>
/// Authoritative role → permission mapping. This is the single place access policy is decided.
/// </summary>
public static class RolePermissions
{
    private static readonly IReadOnlyDictionary<UserRole, IReadOnlySet<Permission>> Map =
        new Dictionary<UserRole, IReadOnlySet<Permission>>
        {
            // Admin: everything.
            [UserRole.Admin] = AllPermissions(),

            // Developer: build, deploy, and operate — but not user management or billing.
            [UserRole.Developer] = new HashSet<Permission>
            {
                Permission.ViewIntegrations,
                Permission.ManageIntegrations,
                Permission.TriggerManualRun,
                Permission.ViewExecutions,
                Permission.ViewSecrets,
                Permission.ManageSecrets,
                Permission.ManagePackages,
                Permission.ManageAgentTokens,
                Permission.ViewAlerts,
                Permission.ManageAlerts
            },

            // Operator: observe and trigger, nothing that changes code or exposes secrets.
            [UserRole.Operator] = new HashSet<Permission>
            {
                Permission.ViewIntegrations,
                Permission.ViewExecutions,
                Permission.TriggerManualRun,
                Permission.ViewAlerts
            },

            // Member (legacy): read-only.
            [UserRole.Member] = new HashSet<Permission>
            {
                Permission.ViewIntegrations,
                Permission.ViewExecutions
            }
        };

    public static bool IsGranted(UserRole role, Permission permission) =>
        Map.TryGetValue(role, out var permissions) && permissions.Contains(permission);

    public static IReadOnlySet<Permission> For(UserRole role) =>
        Map.TryGetValue(role, out var permissions) ? permissions : new HashSet<Permission>();

    private static IReadOnlySet<Permission> AllPermissions() =>
        new HashSet<Permission>(Enum.GetValues<Permission>());
}
