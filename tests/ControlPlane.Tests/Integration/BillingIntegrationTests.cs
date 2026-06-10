using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ControlPlane.Tests.IntegrationTests;

public class BillingIntegrationTests
{
    [Fact]
    public async Task Current_ForNewTenant_ReportsFreePlanAndBillingDisabled()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var token = await SetupAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/api/billing/current");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Free", body.GetProperty("plan").GetString());
        // No Stripe key is configured in tests, so billing is disabled but status still reports.
        Assert.False(body.GetProperty("billingEnabled").GetBoolean());
        Assert.False(body.GetProperty("hasBillingAccount").GetBoolean());
        Assert.True(body.GetProperty("executionLimit").GetInt32() > 0);
    }

    [Fact]
    public async Task Current_RequiresAuthentication()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/billing/current");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_WhenBillingNotConfigured_ReturnsOkAndIgnores()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        content.Headers.Add("Stripe-Signature", "t=1,v1=deadbeef");
        using var response = await client.PostAsync("/api/billing/webhook", content);

        // With no Stripe key configured the webhook is a no-op, but must still ack so Stripe stops retrying.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<string> SetupAdminAsync(HttpClient client)
    {
        using var setup = await client.PostAsJsonAsync("/api/setup", new
        {
            tenantName = "Acme Corp",
            tenantSlug = $"acme-{Guid.NewGuid():N}".Substring(0, 20),
            adminEmail = $"admin-{Guid.NewGuid():N}@acme.com",
            adminPassword = "securepass123"
        });
        setup.EnsureSuccessStatusCode();
        var body = JsonDocument.Parse(await setup.Content.ReadAsStringAsync()).RootElement;
        return body.GetProperty("token").GetString()!;
    }
}
