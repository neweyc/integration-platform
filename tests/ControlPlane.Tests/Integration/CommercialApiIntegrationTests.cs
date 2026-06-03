using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ControlPlane.Tests.IntegrationTests;

public class CommercialApiIntegrationTests
{
    [Fact]
    public async Task RegisterTenant_CreatesSaasTenantAndRejectsDuplicateSlug()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var slug = $"saas-{Guid.NewGuid():N}";
        using var registered = await PostJsonDocumentAsync(
            client,
            "/api/tenants/register",
            new
            {
                TenantName = "SaaS Tenant",
                TenantSlug = slug,
                AdminEmail = "admin@saas.example",
                AdminPassword = "Password123!"
            });

        AssertGuidProperty(registered.RootElement, "tenantId");
        AssertStringProperty(registered.RootElement, "tenantName", "SaaS Tenant");
        AssertGuidProperty(registered.RootElement, "userId");
        AssertStringProperty(registered.RootElement, "email", "admin@saas.example");
        AssertStringProperty(registered.RootElement, "token");

        var duplicate = await client.PostAsJsonAsync(
            "/api/tenants/register",
            new
            {
                TenantName = "Duplicate",
                TenantSlug = slug,
                AdminEmail = "duplicate@saas.example",
                AdminPassword = "Password123!"
            });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task PersonalAccessToken_AuthenticatesApiAndCanBeRevoked()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var setup = await PostJsonAsync<SetupResponse>(
            client,
            "/api/setup",
            new
            {
                TenantName = "Acme",
                TenantSlug = $"acme-{Guid.NewGuid():N}",
                AdminEmail = "admin@example.com",
                AdminPassword = "Password123!"
            });

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.Token);

        using var createdToken = await PostJsonDocumentAsync(
            client,
            "/api/user-tokens",
            new { Name = "CLI token" },
            HttpStatusCode.Created);

        var tokenId = AssertGuidProperty(createdToken.RootElement, "id");
        var plaintextToken = AssertStringProperty(createdToken.RootElement, "plaintextToken");
        Assert.StartsWith("pat_", plaintextToken);

        using var listedTokens = await GetJsonDocumentAsync(client, "/api/user-tokens");
        var listed = Assert.Single(listedTokens.RootElement.GetProperty("tokens").EnumerateArray());
        AssertGuidProperty(listed, "id", tokenId);
        AssertStringProperty(listed, "name", "CLI token");
        Assert.False(listed.TryGetProperty("plaintextToken", out _));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintextToken);

        using var createdIntegration = await PostJsonDocumentAsync(
            client,
            "/api/integrations",
            new
            {
                Name = "PAT Created Job",
                Slug = $"pat-created-job-{Guid.NewGuid():N}",
                Environment = "production",
                TriggerType = "Manual",
                ClassName = "Tests.PatCreatedJob"
            },
            HttpStatusCode.Created);

        var integrationId = AssertGuidProperty(createdIntegration.RootElement, "id");

        using var listViaPat = await GetJsonDocumentAsync(client, "/api/integrations");
        var integration = Assert.Single(listViaPat.RootElement.GetProperty("integrations").EnumerateArray());
        AssertGuidProperty(integration, "id", integrationId);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.Token);
        var revoke = await client.DeleteAsync($"/api/user-tokens/{tokenId}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintextToken);
        var afterRevoke = await client.GetAsync("/api/integrations");
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
    }

    private static async Task<JsonDocument> GetJsonDocumentAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}. Body: {content}");
        return JsonDocument.Parse(content);
    }

    private static async Task<JsonDocument> PostJsonDocumentAsync(
        HttpClient client,
        string url,
        object body,
        HttpStatusCode expectedStatusCode = HttpStatusCode.OK)
    {
        var response = await client.PostAsJsonAsync(url, body);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode}, got {response.StatusCode}. Body: {content}");
        return JsonDocument.Parse(content);
    }

    private static async Task<T> PostJsonAsync<T>(
        HttpClient client,
        string url,
        object body,
        HttpStatusCode expectedStatusCode = HttpStatusCode.OK)
    {
        var response = await client.PostAsJsonAsync(url, body);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode}, got {response.StatusCode}. Body: {content}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static Guid AssertGuidProperty(JsonElement element, string propertyName, Guid? expected = null)
    {
        Assert.True(element.TryGetProperty(propertyName, out var property), $"Missing '{propertyName}'.");
        var value = property.GetGuid();
        if (expected.HasValue)
            Assert.Equal(expected.Value, value);
        return value;
    }

    private static string AssertStringProperty(JsonElement element, string propertyName, string? expected = null)
    {
        Assert.True(element.TryGetProperty(propertyName, out var property), $"Missing '{propertyName}'.");
        var value = property.GetString();
        Assert.NotNull(value);
        if (expected is not null)
            Assert.Equal(expected, value);
        return value!;
    }

    private sealed record SetupResponse(string Token);
}
