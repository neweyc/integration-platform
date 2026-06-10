using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ControlPlane.Tests.IntegrationTests;

public class OnboardingIntegrationTests
{
    [Fact]
    public async Task Status_ForFreshTenant_ReportsAllStepsIncomplete()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var setup = await client.PostAsJsonAsync("/api/setup", new
        {
            tenantName = "Acme Corp",
            tenantSlug = $"acme-{Guid.NewGuid():N}".Substring(0, 20),
            adminEmail = $"admin-{Guid.NewGuid():N}@acme.com",
            adminPassword = "securepass123"
        });
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var token = JsonDocument.Parse(await setup.Content.ReadAsStringAsync())
            .RootElement.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var status = await client.GetAsync("/api/onboarding/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);

        var body = JsonDocument.Parse(await status.Content.ReadAsStringAsync()).RootElement;
        Assert.False(body.GetProperty("complete").GetBoolean());

        var steps = body.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(3, steps.Count);
        Assert.All(steps, s => Assert.False(s.GetProperty("complete").GetBoolean()));
        Assert.Contains(steps, s => s.GetProperty("key").GetString() == "agent-token");
        Assert.Contains(steps, s => s.GetProperty("key").GetString() == "integration");
        Assert.Contains(steps, s => s.GetProperty("key").GetString() == "execution");
    }

    [Fact]
    public async Task Status_RequiresAuthentication()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var status = await client.GetAsync("/api/onboarding/status");
        Assert.Equal(HttpStatusCode.Unauthorized, status.StatusCode);
    }
}
