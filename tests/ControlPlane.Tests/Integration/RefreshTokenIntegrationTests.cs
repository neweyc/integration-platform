using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ControlPlane.Tests.IntegrationTests;

public class RefreshTokenIntegrationTests
{
    [Fact]
    public async Task Refresh_RotatesToken_AndRejectsReuseOfOldToken()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var firstRefresh = await SetupAndGetRefreshTokenAsync(client);

        // Exchange the refresh token for a new pair.
        using var refreshed = await PostAsync(client, "/api/auth/refresh", new { refreshToken = firstRefresh });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var refreshedBody = await ReadJsonAsync(refreshed);
        var secondRefresh = refreshedBody.GetProperty("refreshToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(refreshedBody.GetProperty("accessToken").GetString()));
        Assert.NotEqual(firstRefresh, secondRefresh);

        // The new token works...
        using var secondOk = await PostAsync(client, "/api/auth/refresh", new { refreshToken = secondRefresh });
        Assert.Equal(HttpStatusCode.OK, secondOk.StatusCode);

        // ...but reusing the original (now rotated/revoked) token is rejected.
        using var reused = await PostAsync(client, "/api/auth/refresh", new { refreshToken = firstRefresh });
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var refreshToken = await SetupAndGetRefreshTokenAsync(client);

        using var loggedOut = await PostAsync(client, "/api/auth/logout", new { refreshToken });
        Assert.Equal(HttpStatusCode.NoContent, loggedOut.StatusCode);

        // A revoked token can no longer be exchanged.
        using var afterLogout = await PostAsync(client, "/api/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithUnknownToken_Returns401()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, "/api/auth/refresh", new { refreshToken = "rt_does_not_exist" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<string> SetupAndGetRefreshTokenAsync(HttpClient client)
    {
        using var setup = await PostAsync(client, "/api/setup", new
        {
            tenantName = "Acme Corp",
            tenantSlug = $"acme-{Guid.NewGuid():N}".Substring(0, 20),
            adminEmail = $"admin-{Guid.NewGuid():N}@acme.com",
            adminPassword = "securepass123"
        });
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var body = await ReadJsonAsync(setup);
        var refreshToken = body.GetProperty("refreshToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));
        return refreshToken!;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body) =>
        client.PostAsJsonAsync(path, body);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
