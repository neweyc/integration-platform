namespace ControlPlane.Infrastructure.Authorization;

/// <summary>
/// Fine-grained capabilities that endpoints require. Roles are mapped to sets of
/// these in <see cref="RolePermissions"/> — the single source of truth for access control.
/// </summary>
public enum Permission
{
    // Integrations
    ViewIntegrations,
    ManageIntegrations,   // create / update / delete / repoint package
    TriggerManualRun,

    // Execution history & logs
    ViewExecutions,

    // Secrets
    ViewSecrets,
    ManageSecrets,        // set / delete

    // Deployment artifacts
    ManagePackages,       // upload / delete packages

    // Agent credentials
    ManageAgentTokens,    // create / revoke

    // Tenant administration
    ManageUsers,          // invitations
    ManageBilling,
    ViewAuditLog
}
