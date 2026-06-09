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
    ViewAuditLog,

    // Failure alerting
    ViewAlerts,           // view tenant + per-integration alert configuration
    ManageAlerts,         // edit alert configuration, send test alerts

    // Deployment environments
    ViewEnvironments,     // view the tenant's environment registry
    ManageEnvironments    // create / rename / delete environments
}
