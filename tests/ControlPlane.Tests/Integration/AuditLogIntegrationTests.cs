using System.Net;
using System.Net.Http.Json;

namespace ControlPlane.Tests.IntegrationTests;

/// <summary>
/// End-to-end audit logging: real administrative actions produce audit entries with the correct
/// actor/action/target, secret values never appear, and the audit-log endpoint is Admin-gated.
/// </summary>
public class AuditLogIntegrationTests
{
    [Fact]
    public async Task AdminActions_AreRecorded_WithActorActionTarget_AndNoSecretValues()
    {
        await using var ctx = await RbacContext.CreateAsync();
        if (ctx is null) return;

        ctx.Authenticate(ctx.AdminToken);

        // Action 1: create an integration
        var created = await ctx.Client.PostAsJsonAsync("/api/integrations", new
        {
            Name = "Sync",
            Slug = $"sync-{Guid.NewGuid():N}",
            Environment = "production",
            Triggers = Array.Empty<object>(),
            ClassName = "Acme.Sync"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var integrationId = (await created.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Action 2: set a secret — the value must never be audited
        const string secretValue = "topsecret-value-12345";
        var setSecret = await ctx.Client.PutAsJsonAsync("/api/secrets/production/API_KEY", new { Value = secretValue });
        Assert.Equal(HttpStatusCode.OK, setSecret.StatusCode);

        // Read the audit log
        var log = await ctx.Client.GetFromJsonAsync<AuditLogResponse>("/api/audit-log");
        Assert.NotNull(log);

        var integrationEntry = Assert.Single(log!.Entries, e => e.Action == "IntegrationCreated");
        Assert.Equal(integrationId.ToString(), integrationEntry.TargetId);
        Assert.Contains("admin", integrationEntry.ActorEmail);
        Assert.NotNull(integrationEntry.ActorUserId);

        var secretEntry = Assert.Single(log.Entries, e => e.Action == "SecretSet");
        Assert.Equal("production/API_KEY", secretEntry.TargetId);

        // The secret value must not appear anywhere in the audit log.
        Assert.DoesNotContain(log.Entries, e =>
            (e.Summary ?? "").Contains(secretValue) || (e.TargetId ?? "").Contains(secretValue));
    }

    [Fact]
    public async Task InvitationAcceptance_IsRecorded_WithNewUserAsActor()
    {
        await using var ctx = await RbacContext.CreateAsync();
        if (ctx is null) return;

        // TokenForRoleAsync invites + accepts a Developer — acceptance should be audited.
        await ctx.TokenForRoleAsync("Developer");

        ctx.Authenticate(ctx.AdminToken);
        var log = await ctx.Client.GetFromJsonAsync<AuditLogResponse>("/api/audit-log");

        Assert.Contains(log!.Entries, e => e.Action == "UserInvited");
        Assert.Contains(log.Entries, e => e.Action == "InvitationAccepted");

        var accepted = log.Entries.First(e => e.Action == "InvitationAccepted");
        Assert.Contains("developer", accepted.ActorEmail);
    }

    [Fact]
    public async Task InvitationResendAndRevoke_AreRecorded()
    {
        await using var ctx = await RbacContext.CreateAsync();
        if (ctx is null) return;

        ctx.Authenticate(ctx.AdminToken);

        var invited = await ctx.Client.PostAsJsonAsync("/api/invitations",
            new { Email = $"operator-{Guid.NewGuid():N}@example.com", Role = "Operator" });
        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
        var invitation = (await invited.Content.ReadFromJsonAsync<InviteResponse>())!;

        Assert.Equal(HttpStatusCode.OK,
            (await ctx.Client.PostAsync($"/api/invitations/{invitation.InvitationId}/resend", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await ctx.Client.DeleteAsync($"/api/invitations/{invitation.InvitationId}")).StatusCode);

        var log = await ctx.Client.GetFromJsonAsync<AuditLogResponse>("/api/audit-log");

        Assert.Contains(log!.Entries, e => e.Action == "InvitationResent" && e.TargetId == invitation.InvitationId.ToString());
        Assert.Contains(log.Entries, e => e.Action == "InvitationRevoked" && e.TargetId == invitation.InvitationId.ToString());
    }


    [Fact]
    public async Task PersonalAccessToken_ActionsAreRecorded_WithoutPlaintext()
    {
        await using var ctx = await RbacContext.CreateAsync();
        if (ctx is null) return;

        ctx.Authenticate(ctx.AdminToken);

        var created = await ctx.Client.PostAsJsonAsync("/api/user-tokens", new { Name = "CLI token" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var token = (await created.Content.ReadFromJsonAsync<UserTokenResponse>())!;

        var revoked = await ctx.Client.DeleteAsync($"/api/user-tokens/{token.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        var log = await ctx.Client.GetFromJsonAsync<AuditLogResponse>("/api/audit-log");

        var createdEntry = Assert.Single(log!.Entries, e => e.Action == "UserTokenCreated");
        Assert.Equal(token.Id.ToString(), createdEntry.TargetId);
        Assert.DoesNotContain(token.PlaintextToken, createdEntry.Summary ?? "");
        Assert.DoesNotContain(token.PlaintextToken, createdEntry.TargetId ?? "");

        var revokedEntry = Assert.Single(log.Entries, e => e.Action == "UserTokenRevoked");
        Assert.Equal(token.Id.ToString(), revokedEntry.TargetId);
        Assert.DoesNotContain(token.PlaintextToken, revokedEntry.Summary ?? "");
    }

    [Fact]
    public async Task AuditLogEndpoint_IsForbiddenForNonAdmins()
    {
        await using var ctx = await RbacContext.CreateAsync();
        if (ctx is null) return;

        foreach (var role in new[] { "Developer", "Operator", "Member" })
        {
            var token = await ctx.TokenForRoleAsync(role);
            ctx.Authenticate(token);
            var response = await ctx.Client.GetAsync("/api/audit-log");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // Admin can read it.
        ctx.Authenticate(ctx.AdminToken);
        Assert.Equal(HttpStatusCode.OK, (await ctx.Client.GetAsync("/api/audit-log")).StatusCode);
    }

    private sealed record IdResponse(Guid Id);
    private sealed record InviteResponse(Guid InvitationId, string Email, string Token, DateTime ExpiresAt);
    private sealed record AuditLogResponse(IReadOnlyList<AuditEntry> Entries);
    private sealed record AuditEntry(
        Guid Id, Guid? ActorUserId, string ActorEmail, string Action,
        string TargetType, string? TargetId, string? Summary, DateTime OccurredAt);
    private sealed record UserTokenResponse(Guid Id, string PlaintextToken);
}
