using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ControlPlane.Tests.IntegrationTests;

/// <summary>
/// Proves server-side role enforcement: each role is allowed exactly the operations its
/// permission set grants, and forbidden (403) everything else. Tokens are obtained through
/// the real invite → accept flow, so the JWT role claim is exercised end-to-end.
/// </summary>
public class RbacEnforcementIntegrationTests
{
    [Fact]
    public async Task Operator_CanViewButCannotDeployOrViewSecrets()
    {
        await using var ctx = await RbacContext.CreateAsync();
        if (ctx is null) return;

        var op = await ctx.TokenForRoleAsync("Operator");
        ctx.Authenticate(op);

        // Allowed: view integrations
        Assert.Equal(HttpStatusCode.OK, (await ctx.Client.GetAsync("/api/integrations")).StatusCode);

        // Forbidden: create integration (ManageIntegrations)
        var create = await ctx.Client.PostAsJsonAsync("/api/integrations", NewIntegration());
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        // Forbidden: view secrets (ViewSecrets)
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ctx.Client.GetAsync("/api/secrets/production")).StatusCode);
    }

    [Fact]
    public async Task Developer_CanDeployAndManageSecrets_ButNotInviteUsers()
    {
        await using var ctx = await RbacContext.CreateAsync();
        if (ctx is null) return;

        var dev = await ctx.TokenForRoleAsync("Developer");
        ctx.Authenticate(dev);

        // Allowed: create integration
        var create = await ctx.Client.PostAsJsonAsync("/api/integrations", NewIntegration());
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        // Allowed: manage secrets
        var setSecret = await ctx.Client.PutAsJsonAsync("/api/secrets/production/API_KEY", new { Value = "abc123" });
        Assert.Equal(HttpStatusCode.OK, setSecret.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ctx.Client.GetAsync("/api/secrets/production")).StatusCode);

        // Forbidden: invite users (ManageUsers)
        var invite = await ctx.Client.PostAsJsonAsync("/api/invitations",
            new { Email = $"x-{Guid.NewGuid():N}@example.com", Role = "Operator" });
        Assert.Equal(HttpStatusCode.Forbidden, invite.StatusCode);

        // Forbidden: legacy direct user registration is now user-management gated.
        var register = await ctx.Client.PostAsJsonAsync("/api/auth/register",
            new { Email = $"direct-{Guid.NewGuid():N}@example.com", Password = "Password123!" });
        Assert.Equal(HttpStatusCode.Forbidden, register.StatusCode);
    }

    [Fact]
    public async Task Member_IsReadOnly_CannotTriggerManualRun()
    {
        await using var ctx = await RbacContext.CreateAsync();
        if (ctx is null) return;

        // Admin creates an integration first
        ctx.Authenticate(ctx.AdminToken);
        var created = await ctx.Client.PostAsJsonAsync("/api/integrations", NewIntegration());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var integrationId = (await created.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var member = await ctx.TokenForRoleAsync("Member");
        ctx.Authenticate(member);

        // Allowed: view
        Assert.Equal(HttpStatusCode.OK, (await ctx.Client.GetAsync("/api/integrations")).StatusCode);

        // Forbidden: trigger manual run (TriggerManualRun)
        var run = await ctx.Client.PostAsync($"/api/integrations/{integrationId}/run", null);
        Assert.Equal(HttpStatusCode.Forbidden, run.StatusCode);
    }

    [Fact]
    public async Task Admin_CanDoEverything()
    {
        await using var ctx = await RbacContext.CreateAsync();
        if (ctx is null) return;

        ctx.Authenticate(ctx.AdminToken);

        Assert.Equal(HttpStatusCode.Created,
            (await ctx.Client.PostAsJsonAsync("/api/integrations", NewIntegration())).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await ctx.Client.PutAsJsonAsync("/api/secrets/production/K", new { Value = "v" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await ctx.Client.PostAsJsonAsync("/api/invitations",
                new { Email = $"a-{Guid.NewGuid():N}@example.com", Role = "Developer" })).StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_Gets401_NotForbidden()
    {
        await using var ctx = await RbacContext.CreateAsync();
        if (ctx is null) return;

        ctx.Client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await ctx.Client.GetAsync("/api/integrations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await ctx.Client.PostAsJsonAsync("/api/auth/register",
                new { Email = $"direct-{Guid.NewGuid():N}@example.com", Password = "Password123!" })).StatusCode);
    }

    [Fact]
    public async Task UserTokenRevoke_IsScopedToOwningUser()
    {
        await using var ctx = await RbacContext.CreateAsync();
        if (ctx is null) return;

        ctx.Authenticate(ctx.AdminToken);
        var createdToken = await ctx.Client.PostAsJsonAsync("/api/user-tokens", new { Name = "Admin CLI" });
        Assert.Equal(HttpStatusCode.Created, createdToken.StatusCode);

        var token = (await createdToken.Content.ReadFromJsonAsync<UserTokenResponse>())!;

        var operatorToken = await ctx.TokenForRoleAsync("Operator");
        ctx.Authenticate(operatorToken);

        // The endpoint is intentionally idempotent, but this must not delete another user's token.
        Assert.Equal(HttpStatusCode.NoContent,
            (await ctx.Client.DeleteAsync($"/api/user-tokens/{token.Id}")).StatusCode);

        ctx.Authenticate(token.PlaintextToken);
        Assert.Equal(HttpStatusCode.OK, (await ctx.Client.GetAsync("/api/integrations")).StatusCode);

        ctx.Authenticate(ctx.AdminToken);
        Assert.Equal(HttpStatusCode.NoContent,
            (await ctx.Client.DeleteAsync($"/api/user-tokens/{token.Id}")).StatusCode);

        ctx.Authenticate(token.PlaintextToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ctx.Client.GetAsync("/api/integrations")).StatusCode);
    }

    private static object NewIntegration() => new
    {
        Name = "Job",
        Slug = $"job-{Guid.NewGuid():N}",
        Environment = "production",
        TriggerType = "Manual",
        ClassName = "Acme.Job"
    };

    private sealed record IdResponse(Guid Id);
    private sealed record UserTokenResponse(Guid Id, string PlaintextToken);
}
