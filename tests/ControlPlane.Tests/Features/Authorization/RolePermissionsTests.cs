using ControlPlane.Infrastructure.Authorization;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Authorization;

public class RolePermissionsTests
{
    [Fact]
    public void Admin_HasEveryPermission()
    {
        foreach (var permission in Enum.GetValues<Permission>())
            Assert.True(RolePermissions.IsGranted(UserRole.Admin, permission),
                $"Admin should have {permission}");
    }

    [Theory]
    [InlineData(Permission.ViewIntegrations, true)]
    [InlineData(Permission.ManageIntegrations, true)]
    [InlineData(Permission.TriggerManualRun, true)]
    [InlineData(Permission.ViewExecutions, true)]
    [InlineData(Permission.ViewSecrets, true)]
    [InlineData(Permission.ManageSecrets, true)]
    [InlineData(Permission.ManagePackages, true)]
    [InlineData(Permission.ManageAgentTokens, true)]
    [InlineData(Permission.ManageUsers, false)]
    [InlineData(Permission.ManageBilling, false)]
    [InlineData(Permission.ViewAuditLog, false)]
    public void Developer_CanDeployButNotAdminister(Permission permission, bool granted)
    {
        Assert.Equal(granted, RolePermissions.IsGranted(UserRole.Developer, permission));
    }

    [Theory]
    [InlineData(Permission.ViewIntegrations, true)]
    [InlineData(Permission.ViewExecutions, true)]
    [InlineData(Permission.TriggerManualRun, true)]
    [InlineData(Permission.ViewSecrets, false)]
    [InlineData(Permission.ManageSecrets, false)]
    [InlineData(Permission.ManageIntegrations, false)]
    [InlineData(Permission.ManagePackages, false)]
    [InlineData(Permission.ManageAgentTokens, false)]
    [InlineData(Permission.ManageUsers, false)]
    [InlineData(Permission.ManageBilling, false)]
    [InlineData(Permission.ViewAuditLog, false)]
    public void Operator_CanObserveAndTriggerOnly(Permission permission, bool granted)
    {
        Assert.Equal(granted, RolePermissions.IsGranted(UserRole.Operator, permission));
    }

    [Theory]
    [InlineData(Permission.ViewIntegrations, true)]
    [InlineData(Permission.ViewExecutions, true)]
    [InlineData(Permission.TriggerManualRun, false)]
    [InlineData(Permission.ManageIntegrations, false)]
    [InlineData(Permission.ViewSecrets, false)]
    [InlineData(Permission.ManageSecrets, false)]
    [InlineData(Permission.ViewAuditLog, false)]
    public void Member_IsReadOnly(Permission permission, bool granted)
    {
        Assert.Equal(granted, RolePermissions.IsGranted(UserRole.Member, permission));
    }

    [Fact]
    public void Operator_CannotViewSecrets_ButDeveloperCan()
    {
        Assert.False(RolePermissions.IsGranted(UserRole.Operator, Permission.ViewSecrets));
        Assert.True(RolePermissions.IsGranted(UserRole.Developer, Permission.ViewSecrets));
    }
}
