using System.Net;
using System.Net.Http.Json;

namespace ControlPlane.Tests.IntegrationTests;

public class RateLimitingIntegrationTests
{
    [Fact]
    public async Task Login_BeyondAuthLimit_Returns429()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        // Re-enable rate limiting (the base factory disables it) with a tiny auth budget so a
        // handful of requests is enough to trip the limiter deterministically.
        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString,
            new Dictionary<string, string?>
            {
                ["RateLimit:Enabled"] = "true",
                ["RateLimit:AuthPermitLimit"] = "3",
                ["RateLimit:AuthWindowSeconds"] = "60"
            });

        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { Email = "nobody@example.com", Password = "wrong-password" });
            statuses.Add(response.StatusCode);
        }

        // The first few attempts are processed (invalid credentials → 400); once the auth window
        // budget is spent the limiter short-circuits with 429 before reaching the handler.
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task Healthz_IsNeverRateLimited()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString,
            new Dictionary<string, string?>
            {
                ["RateLimit:Enabled"] = "true",
                ["RateLimit:PermitLimit"] = "2",
                ["RateLimit:WindowSeconds"] = "60"
            });

        using var client = factory.CreateClient();

        // Even with a global limit of 2, health stays reachable because it opts out.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await client.GetAsync("/healthz");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
