using ControlPlane.Features.AgentTokens;
using ControlPlane.Features.IntegrationPackages;
using ControlPlane.Features.Integrations;
using ControlPlane.Features.Invitations;
using ControlPlane.Features.Secrets;
using ControlPlane.Features.UserTokens;
using ControlPlane.Infrastructure.Auditing;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Auditing;

public class AuditableCommandTests
{
    [Fact]
    public void SetSecret_Describes_WithoutLeakingValue()
    {
        var cmd = new SetSecretCommand(Guid.NewGuid(), "production", "API_KEY", "super-secret-value");

        var d = cmd.Describe(new SetSecretResult(Guid.NewGuid(), "production", "API_KEY", DateTime.UtcNow));

        Assert.NotNull(d);
        Assert.Equal(AuditAction.SecretSet, d!.Action);
        Assert.Equal("Secret", d.TargetType);
        Assert.Equal("production/API_KEY", d.TargetId);
        // The secret value must never appear anywhere in the audit descriptor.
        Assert.DoesNotContain("super-secret-value", d.Summary ?? "");
        Assert.DoesNotContain("super-secret-value", d.TargetId ?? "");
    }

    [Fact]
    public void DeleteSecret_Describes_WithoutLeakingValue()
    {
        var cmd = new DeleteSecretCommand(Guid.NewGuid(), "staging", "DB_PASSWORD");

        var d = cmd.Describe(true);

        Assert.Equal(AuditAction.SecretDeleted, d!.Action);
        Assert.Equal("staging/DB_PASSWORD", d.TargetId);
    }

    [Fact]
    public void CreateIntegration_UsesResultIdAsTarget()
    {
        var cmd = new CreateIntegrationCommand(
            Guid.NewGuid(), "Sync", "sync", null, "production", "Acme.Sync", []);
        var id = Guid.NewGuid();
        var result = new CreateIntegrationResult(id, "Sync", "sync", "production", "Enabled", "Acme.Sync", []);

        var d = cmd.Describe(result);

        Assert.Equal(AuditAction.IntegrationCreated, d!.Action);
        Assert.Equal(id.ToString(), d.TargetId);
    }

    [Fact]
    public void CreateAgentToken_DoesNotLeakTokenPlaintext()
    {
        var cmd = new CreateAgentTokenCommand(Guid.NewGuid(), "Prod Agent", "production");
        var result = new CreateAgentTokenResult(Guid.NewGuid(), "Prod Agent", "production", "agt_PLAINTEXTSECRET", DateTime.UtcNow);

        var d = cmd.Describe(result);

        Assert.Equal(AuditAction.AgentTokenCreated, d!.Action);
        Assert.DoesNotContain("agt_PLAINTEXTSECRET", d.Summary ?? "");
        Assert.Equal(result.Id.ToString(), d.TargetId);
    }

    [Fact]
    public void CreateUserToken_DoesNotLeakTokenPlaintext()
    {
        var cmd = new CreateUserTokenCommand(Guid.NewGuid(), Guid.NewGuid(), "CLI token");
        var result = new CreateUserTokenResult(Guid.NewGuid(), "CLI token", "pat_PLAINTEXTSECRET", DateTime.UtcNow);

        var d = cmd.Describe(result);

        Assert.Equal(AuditAction.UserTokenCreated, d!.Action);
        Assert.DoesNotContain("pat_PLAINTEXTSECRET", d.Summary ?? "");
        Assert.Equal(result.Id.ToString(), d.TargetId);
    }

    [Fact]
    public void RevokeUserToken_OnlyAuditsWhenDeleted()
    {
        var cmd = new RevokeUserTokenCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(cmd.Describe(false));

        var d = cmd.Describe(true);
        Assert.Equal(AuditAction.UserTokenRevoked, d!.Action);
        Assert.Equal("UserToken", d.TargetType);
    }

    [Fact]
    public void UploadPackage_DescribesNameAndVersion()
    {
        var cmd = new UploadPackageCommand(Guid.NewGuid(), "Acme.Pkg", "1.2.3", "pkg.zip", [1, 2, 3]);
        var id = Guid.NewGuid();
        var result = new PackageUploadResult(
            new PackageMetadata(id, "Acme.Pkg", "1.2.3", "pkg.zip", 3, "hash", DateTime.UtcNow),
            []);

        var d = cmd.Describe(result);

        Assert.Equal(AuditAction.PackageUploaded, d!.Action);
        Assert.Equal(id.ToString(), d.TargetId);
        Assert.Contains("1.2.3", d.Summary);
    }

    [Fact]
    public void DeletePackage_OnlyAuditsWhenDeleted()
    {
        var cmd = new DeletePackageCommand(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(cmd.Describe(false));

        var d = cmd.Describe(true);
        Assert.Equal(AuditAction.PackageDeleted, d!.Action);
        Assert.Equal("Package", d.TargetType);
    }

    [Fact]
    public void InviteUser_DescribesEmailAndRole()
    {
        var cmd = new InviteUserCommand("new@example.com", UserRole.Developer);
        var result = new InviteUserResult(Guid.NewGuid(), "new@example.com", "tok", DateTime.UtcNow);

        var d = cmd.Describe(result);

        Assert.Equal(AuditAction.UserInvited, d!.Action);
        Assert.Equal(result.InvitationId.ToString(), d.TargetId);
        Assert.Contains("Developer", d.Summary);
    }

    [Fact]
    public void RevokeInvitation_OnlyAuditsWhenRevoked()
    {
        var invitationId = Guid.NewGuid();
        var cmd = new RevokeInvitationCommand(Guid.NewGuid(), invitationId);

        Assert.Null(cmd.Describe(false));

        var d = cmd.Describe(true);
        Assert.Equal(AuditAction.InvitationRevoked, d!.Action);
        Assert.Equal("Invitation", d.TargetType);
        Assert.Equal(invitationId.ToString(), d.TargetId);
    }

    [Fact]
    public void ResendInvitation_DoesNotLeakToken()
    {
        var invitationId = Guid.NewGuid();
        var cmd = new ResendInvitationCommand(Guid.NewGuid(), invitationId);
        var result = new ResendInvitationResult(invitationId, "new@example.com", "Developer", "invite_PLAINTEXT", DateTime.UtcNow);

        var d = cmd.Describe(result);

        Assert.Equal(AuditAction.InvitationResent, d!.Action);
        Assert.Equal(invitationId.ToString(), d.TargetId);
        Assert.DoesNotContain("invite_PLAINTEXT", d.Summary ?? "");
        Assert.DoesNotContain("invite_PLAINTEXT", d.TargetId ?? "");
    }
}
