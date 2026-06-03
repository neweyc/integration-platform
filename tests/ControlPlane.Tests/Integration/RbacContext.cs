using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ControlPlane.Tests.IntegrationTests;

/// <summary>
/// Test harness for RBAC scenarios: spins up the app against a real DB, sets up a tenant +
/// admin, and mints role-scoped JWTs through the real invite → accept flow.
/// Returns null when no test database is available (mirrors the other integration tests).
/// </summary>
internal sealed class RbacContext : IAsyncDisposable
{
    private readonly IntegrationTestDatabase _database;
    private readonly ControlPlaneWebApplicationFactory _factory;

    public HttpClient Client { get; }
    public string AdminToken { get; }

    private RbacContext(
        IntegrationTestDatabase database,
        ControlPlaneWebApplicationFactory factory,
        HttpClient client,
        string adminToken)
    {
        _database = database;
        _factory = factory;
        Client = client;
        AdminToken = adminToken;
    }

    public static async Task<RbacContext?> CreateAsync()
    {
        var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return null;

        var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        var client = factory.CreateClient();

        var setup = await PostAsync<SetupResponse>(client, "/api/setup", new
        {
            TenantName = "Acme",
            TenantSlug = $"acme-{Guid.NewGuid():N}",
            AdminEmail = $"admin-{Guid.NewGuid():N}@example.com",
            AdminPassword = "Password123!"
        });

        return new RbacContext(database, factory, client, setup.Token);
    }

    public void Authenticate(string token) =>
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    /// <summary>Invites a user with the given role (as admin) and accepts it, returning a role-scoped JWT.</summary>
    public async Task<string> TokenForRoleAsync(string role)
    {
        Authenticate(AdminToken);

        var email = $"{role.ToLower()}-{Guid.NewGuid():N}@example.com";
        var invite = await PostAsync<InviteResponse>(Client, "/api/invitations",
            new { Email = email, Role = role });

        var accepted = await PostAsync<AcceptResponse>(Client, "/api/invitations/accept",
            new { Token = invite.Token, Password = "Password123!" });

        return accepted.Token;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"POST {url} failed: {response.StatusCode}. Body: {content}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    private sealed record SetupResponse(Guid TenantId, string Token);
    private sealed record InviteResponse(Guid InvitationId, string Email, string Token, DateTime ExpiresAt);
    private sealed record AcceptResponse(Guid UserId, string Email, string Token);
}
