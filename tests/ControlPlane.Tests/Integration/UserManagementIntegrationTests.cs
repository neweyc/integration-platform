using System.Net;
using System.Net.Http.Json;

namespace ControlPlane.Tests.IntegrationTests;

public class UserManagementIntegrationTests
{
    [Fact]
    public async Task Admin_CanListUsersAndPendingInvitations()
    {
        await using var ctx = await RbacContext.CreateAsync();
        if (ctx is null) return;

        ctx.Authenticate(ctx.AdminToken);

        var invited = await ctx.Client.PostAsJsonAsync("/api/invitations",
            new { Email = $"operator-{Guid.NewGuid():N}@example.com", Role = "Operator" });
        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
        var invitation = (await invited.Content.ReadFromJsonAsync<InviteResponse>())!;

        var users = await ctx.Client.GetFromJsonAsync<ListUsersResponse>("/api/auth/users");
        Assert.NotNull(users);
        Assert.Contains(users!.Users, u => u.Role == "Admin" && u.Email.Contains("admin"));

        var invitations = await ctx.Client.GetFromJsonAsync<ListInvitationsResponse>("/api/invitations");
        Assert.NotNull(invitations);
        var pending = Assert.Single(invitations!.Invitations, i => i.Id == invitation.InvitationId);
        Assert.Equal(invitation.Email, pending.Email);
        Assert.Equal("Operator", pending.Role);
        Assert.Null(pending.AcceptedAt);
    }

    [Fact]
    public async Task AcceptedInvitation_IsNoLongerListedAsPending_AndUserIsListed()
    {
        await using var ctx = await RbacContext.CreateAsync();
        if (ctx is null) return;

        ctx.Authenticate(ctx.AdminToken);

        var email = $"developer-{Guid.NewGuid():N}@example.com";
        var invited = await ctx.Client.PostAsJsonAsync("/api/invitations",
            new { Email = email, Role = "Developer" });
        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
        var invitation = (await invited.Content.ReadFromJsonAsync<InviteResponse>())!;

        var accepted = await ctx.Client.PostAsJsonAsync("/api/invitations/accept",
            new { Token = invitation.Token, Password = "Password123!" });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        ctx.Authenticate(ctx.AdminToken);
        var invitations = await ctx.Client.GetFromJsonAsync<ListInvitationsResponse>("/api/invitations");
        Assert.DoesNotContain(invitations!.Invitations, i => i.Id == invitation.InvitationId);

        var users = await ctx.Client.GetFromJsonAsync<ListUsersResponse>("/api/auth/users");
        Assert.Contains(users!.Users, u => u.Email == email && u.Role == "Developer");
    }

    [Fact]
    public async Task UserManagementLists_AreAdminOnly()
    {
        await using var ctx = await RbacContext.CreateAsync();
        if (ctx is null) return;

        foreach (var role in new[] { "Developer", "Operator", "Member" })
        {
            var token = await ctx.TokenForRoleAsync(role);
            ctx.Authenticate(token);

            Assert.Equal(HttpStatusCode.Forbidden, (await ctx.Client.GetAsync("/api/auth/users")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ctx.Client.GetAsync("/api/invitations")).StatusCode);
        }

        ctx.Authenticate(ctx.AdminToken);
        Assert.Equal(HttpStatusCode.OK, (await ctx.Client.GetAsync("/api/auth/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ctx.Client.GetAsync("/api/invitations")).StatusCode);
    }

    [Fact]
    public async Task Admin_CanRevokePendingInvitation()
    {
        await using var ctx = await RbacContext.CreateAsync();
        if (ctx is null) return;

        ctx.Authenticate(ctx.AdminToken);

        var invited = await ctx.Client.PostAsJsonAsync("/api/invitations",
            new { Email = $"operator-{Guid.NewGuid():N}@example.com", Role = "Operator" });
        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
        var invitation = (await invited.Content.ReadFromJsonAsync<InviteResponse>())!;

        Assert.Equal(HttpStatusCode.NoContent,
            (await ctx.Client.DeleteAsync($"/api/invitations/{invitation.InvitationId}")).StatusCode);

        var invitations = await ctx.Client.GetFromJsonAsync<ListInvitationsResponse>("/api/invitations");
        Assert.DoesNotContain(invitations!.Invitations, i => i.Id == invitation.InvitationId);

        var accepted = await ctx.Client.PostAsJsonAsync("/api/invitations/accept",
            new { Token = invitation.Token, Password = "Password123!" });
        Assert.Equal(HttpStatusCode.BadRequest, accepted.StatusCode);
    }

    [Fact]
    public async Task Admin_CanResendPendingInvitation_RotatingToken()
    {
        await using var ctx = await RbacContext.CreateAsync();
        if (ctx is null) return;

        ctx.Authenticate(ctx.AdminToken);

        var email = $"developer-{Guid.NewGuid():N}@example.com";
        var invited = await ctx.Client.PostAsJsonAsync("/api/invitations",
            new { Email = email, Role = "Developer" });
        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
        var invitation = (await invited.Content.ReadFromJsonAsync<InviteResponse>())!;

        var resentResponse = await ctx.Client.PostAsync($"/api/invitations/{invitation.InvitationId}/resend", null);
        Assert.Equal(HttpStatusCode.OK, resentResponse.StatusCode);
        var resent = (await resentResponse.Content.ReadFromJsonAsync<ResendInvitationResponse>())!;

        Assert.Equal(invitation.InvitationId, resent.InvitationId);
        Assert.Equal(email, resent.Email);
        Assert.Equal("Developer", resent.Role);
        Assert.NotEqual(invitation.Token, resent.Token);

        var oldAccept = await ctx.Client.PostAsJsonAsync("/api/invitations/accept",
            new { Token = invitation.Token, Password = "Password123!" });
        Assert.Equal(HttpStatusCode.BadRequest, oldAccept.StatusCode);

        var newAccept = await ctx.Client.PostAsJsonAsync("/api/invitations/accept",
            new { Token = resent.Token, Password = "Password123!" });
        Assert.Equal(HttpStatusCode.OK, newAccept.StatusCode);
    }

    [Fact]
    public async Task InvitationLifecycleActions_AreAdminOnly()
    {
        await using var ctx = await RbacContext.CreateAsync();
        if (ctx is null) return;

        ctx.Authenticate(ctx.AdminToken);
        var invited = await ctx.Client.PostAsJsonAsync("/api/invitations",
            new { Email = $"member-{Guid.NewGuid():N}@example.com", Role = "Member" });
        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
        var invitation = (await invited.Content.ReadFromJsonAsync<InviteResponse>())!;

        var developerToken = await ctx.TokenForRoleAsync("Developer");
        ctx.Authenticate(developerToken);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await ctx.Client.PostAsync($"/api/invitations/{invitation.InvitationId}/resend", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ctx.Client.DeleteAsync($"/api/invitations/{invitation.InvitationId}")).StatusCode);
    }

    private sealed record InviteResponse(Guid InvitationId, string Email, string Token, DateTime ExpiresAt);
    private sealed record ResendInvitationResponse(Guid InvitationId, string Email, string Role, string Token, DateTime ExpiresAt);
    private sealed record ListUsersResponse(IReadOnlyList<UserItem> Users);
    private sealed record UserItem(Guid Id, string Email, string Role, DateTime CreatedAt);
    private sealed record ListInvitationsResponse(IReadOnlyList<InvitationItem> Invitations);
    private sealed record InvitationItem(Guid Id, string Email, string Role, DateTime ExpiresAt, DateTime? AcceptedAt);
}
