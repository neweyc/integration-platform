using System.Net;
using System.Net.Http.Json;

namespace ControlPlane.Tests.IntegrationTests;

public class HealthEndpointsIntegrationTests
{
    [Fact]
    public async Task Healthz_ReturnsHealthy_WithoutAuthentication()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        // No Authorization header — liveness must be reachable anonymously.
        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("healthy", body!.Status);
    }

    [Fact]
    public async Task Readyz_ReturnsReady_WhenDatabaseIsReachable()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReadinessResponse>();
        Assert.Equal("ready", body!.Status);
        Assert.Equal("up", body.Database);
    }

    private sealed record HealthResponse(string Status);

    private sealed record ReadinessResponse(string Status, string Database);
}
