using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Infrastructure;
using Shared.Domain;

namespace ControlPlane.Tests.IntegrationTests;

public class ApiContractAndSecurityIntegrationTests
{
    [Fact]
    public async Task CoreApiResponses_PreserveClientContractShape()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var setup = await SetupTenantAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.Token);

        using var createdIntegration = await PostJsonDocumentAsync(
            client,
            "/api/integrations",
            new
            {
                Name = "Contract Job",
                Slug = $"contract-job-{Guid.NewGuid():N}",
                Description = "Contract test integration",
                Environment = "production",
                Triggers = Array.Empty<object>(),
                ClassName = "Tests.ContractJob",
                TimeoutSeconds = 120,
                RetryMaxAttempts = 2,
                RetryBackoffSeconds = 30
            },
            HttpStatusCode.Created);

        var integration = createdIntegration.RootElement;
        var integrationId = AssertGuidProperty(integration, "id");
        AssertStringProperty(integration, "name", "Contract Job");
        AssertStringProperty(integration, "slug");
        AssertStringProperty(integration, "environment", "production");
        AssertStringProperty(integration, "status", "Enabled");
        AssertStringProperty(integration, "className", "Tests.ContractJob");
        Assert.Empty(integration.GetProperty("triggers").EnumerateArray());
        AssertNumberProperty(integration, "timeoutSeconds", 120);
        AssertNumberProperty(integration, "retryMaxAttempts", 2);
        AssertNumberProperty(integration, "retryBackoffSeconds", 30);

        using var listIntegrations = await GetJsonDocumentAsync(client, "/api/integrations");
        var listItem = Assert.Single(listIntegrations.RootElement.GetProperty("integrations").EnumerateArray());
        AssertGuidProperty(listItem, "id", integrationId);
        AssertStringProperty(listItem, "status", "Enabled");
        Assert.Empty(listItem.GetProperty("triggers").EnumerateArray());
        AssertNumberProperty(listItem, "timeoutSeconds", 120);
        AssertNumberProperty(listItem, "retryMaxAttempts", 2);
        AssertNumberProperty(listItem, "retryBackoffSeconds", 30);
        Assert.True(listItem.TryGetProperty("lastExecution", out _));

        using var triggerAdapters = await GetJsonDocumentAsync(client, "/api/trigger-adapters");
        var adapters = triggerAdapters.RootElement.GetProperty("adapters").EnumerateArray().ToList();
        Assert.Contains(adapters, a =>
            AssertStringProperty(a, "key") == "scheduled"
            && AssertStringProperty(a, "source") == "Scheduled"
            && AssertStringProperty(a, "triggerType") == "Scheduled"
            && AssertBoolProperty(a, "requiresStoredTrigger"));
        Assert.Contains(adapters, a =>
            AssertStringProperty(a, "key") == "queue"
            && AssertStringProperty(a, "source") == "Queue"
            && AssertStringProperty(a, "triggerType") == "Queue"
            && AssertBoolProperty(a, "supportsPayload")
            && AssertBoolProperty(a, "supportsDeduplication"));

        using var createdToken = await PostJsonDocumentAsync(
            client,
            "/api/agent-tokens",
            new { Name = "Production agent", Environment = "production" },
            HttpStatusCode.Created);
        var token = createdToken.RootElement;
        var agentTokenId = AssertGuidProperty(token, "id");
        var plaintextAgentToken = AssertStringProperty(token, "token");
        Assert.StartsWith("agt_", plaintextAgentToken);

        using var listedTokens = await GetJsonDocumentAsync(client, "/api/agent-tokens");
        var listedToken = Assert.Single(listedTokens.RootElement.GetProperty("tokens").EnumerateArray());
        AssertGuidProperty(listedToken, "id", agentTokenId);
        AssertStringProperty(listedToken, "name", "Production agent");
        AssertStringProperty(listedToken, "environment", "production");
        Assert.True(listedToken.TryGetProperty("createdAt", out _));
        Assert.False(listedToken.TryGetProperty("token", out _));

        using var manualRun = await PostJsonDocumentAsync(
            client,
            $"/api/integrations/{integrationId}/run",
            new { },
            HttpStatusCode.Accepted);
        AssertGuidProperty(manualRun.RootElement, "requestId");
        AssertGuidProperty(manualRun.RootElement, "integrationId", integrationId);
        AssertStringProperty(manualRun.RootElement, "environment", "production");
        Assert.True(manualRun.RootElement.TryGetProperty("requestedAt", out _));

        using var triggerEvents = await GetJsonDocumentAsync(client, $"/api/trigger-events?integrationId={integrationId}");
        var triggerEvent = Assert.Single(triggerEvents.RootElement.GetProperty("events").EnumerateArray());
        AssertGuidProperty(triggerEvent, "integrationId", integrationId);
        AssertStringProperty(triggerEvent, "adapterKey", "manual");
        AssertStringProperty(triggerEvent, "source", "Manual");
        AssertStringProperty(triggerEvent, "outcome", "ConvertedToWork");
        AssertGuidProperty(triggerEvent, "workItemId");
        Assert.True(triggerEvent.TryGetProperty("receivedAt", out _));

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Agent-Token", plaintextAgentToken);

        using var poll = await GetJsonDocumentAsync(client, "/api/agent/integrations");
        var pollItem = Assert.Single(poll.RootElement.GetProperty("integrations").EnumerateArray());
        var workItemId = AssertGuidProperty(pollItem, "workItemId");
        AssertGuidProperty(pollItem, "id", integrationId);
        AssertStringProperty(pollItem, "triggerSource", "Manual");
        Assert.True(pollItem.TryGetProperty("leaseExpiresAt", out _));

        using var started = await PostJsonDocumentAsync(
            client,
            "/api/agent/executions",
            new { WorkItemId = workItemId },
            HttpStatusCode.Created);
        var executionId = AssertGuidProperty(started.RootElement, "executionId");
        Assert.True(started.RootElement.TryGetProperty("startedAt", out _));

        var complete = await client.PutAsJsonAsync(
            $"/api/agent/executions/{executionId}",
            new { Succeeded = false, ErrorMessage = "contract failure", Retryable = true });
        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);

        client.DefaultRequestHeaders.Remove("X-Agent-Token");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.Token);

        using var executions = await GetJsonDocumentAsync(client, $"/api/integrations/{integrationId}/executions");
        var execution = Assert.Single(executions.RootElement.GetProperty("executions").EnumerateArray());
        AssertGuidProperty(execution, "id", executionId);
        AssertStringProperty(execution, "status", "Failed");
        AssertStringProperty(execution, "environment", "production");
        AssertStringProperty(execution, "errorMessage", "contract failure");
        AssertNumberProperty(execution, "attemptNumber", 1);
        Assert.True(execution.TryGetProperty("parentExecutionId", out _));
        Assert.True(execution.TryGetProperty("rootExecutionId", out _));

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Agent-Token", plaintextAgentToken);
        var heartbeat = await client.PostAsJsonAsync("/api/agent/heartbeat", new
        {
            Version = "1.0.0",
            Hostname = "worker-01",
            CurrentConcurrency = 1,
            MaxConcurrency = 5
        });
        Assert.Equal(HttpStatusCode.NoContent, heartbeat.StatusCode);

        client.DefaultRequestHeaders.Remove("X-Agent-Token");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.Token);

        using var heartbeats = await GetJsonDocumentAsync(client, "/api/agent-tokens/heartbeats");
        var heartbeatItem = Assert.Single(heartbeats.RootElement.GetProperty("agents").EnumerateArray());
        AssertGuidProperty(heartbeatItem, "agentTokenId", agentTokenId);
        AssertStringProperty(heartbeatItem, "environment", "production");
        AssertStringProperty(heartbeatItem, "version", "1.0.0");
        AssertStringProperty(heartbeatItem, "hostname", "worker-01");
        AssertNumberProperty(heartbeatItem, "currentConcurrency", 1);
        AssertNumberProperty(heartbeatItem, "maxConcurrency", 5);
        Assert.False(heartbeatItem.GetProperty("isStale").GetBoolean());
    }

    [Fact]
    public async Task JwtTenant_CannotReadOrMutateAnotherTenantIntegration()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var tenantA = await SetupTenantAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantA.Token);
        var integrationA = await PostJsonAsync<IntegrationResponse>(
            client,
            "/api/integrations",
            new
            {
                Name = "Tenant A Job",
                Slug = $"tenant-a-job-{Guid.NewGuid():N}",
                Environment = "production",
                Triggers = Array.Empty<object>(),
                ClassName = "Tests.TenantAJob"
            },
            HttpStatusCode.Created);

        var tenantB = await SeedTenantAndLoginAsync(database, client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantB.Token);

        using var tenantBList = await GetJsonDocumentAsync(client, "/api/integrations");
        Assert.Empty(tenantBList.RootElement.GetProperty("integrations").EnumerateArray());

        var getForeign = await client.GetAsync($"/api/integrations/{integrationA.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getForeign.StatusCode);

        var runForeign = await client.PostAsJsonAsync($"/api/integrations/{integrationA.Id}/run", new { });
        Assert.Equal(HttpStatusCode.NotFound, runForeign.StatusCode);

        var deleteForeign = await client.DeleteAsync($"/api/integrations/{integrationA.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteForeign.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantA.Token);
        var stillExists = await client.GetAsync($"/api/integrations/{integrationA.Id}");
        Assert.Equal(HttpStatusCode.OK, stillExists.StatusCode);
    }

    [Fact]
    public async Task AgentToken_CannotStartOrCompleteForeignOrWrongEnvironmentWork()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var tenantA = await SetupTenantAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantA.Token);
        var integrationA = await PostJsonAsync<IntegrationResponse>(
            client,
            "/api/integrations",
            new
            {
                Name = "Production Job",
                Slug = $"production-job-{Guid.NewGuid():N}",
                Environment = "production",
                Triggers = Array.Empty<object>(),
                ClassName = "Tests.ProductionJob"
            },
            HttpStatusCode.Created);

        var productionToken = await PostJsonAsync<AgentTokenResponse>(
            client,
            "/api/agent-tokens",
            new { Name = "Production agent", Environment = "production" },
            HttpStatusCode.Created);
        var secondProductionToken = await PostJsonAsync<AgentTokenResponse>(
            client,
            "/api/agent-tokens",
            new { Name = "Other production agent", Environment = "production" },
            HttpStatusCode.Created);
        // A token can only be scoped to an environment that exists in the registry.
        var stagingEnv = await client.PostAsJsonAsync("/api/environments", new { Name = "staging" });
        Assert.Equal(HttpStatusCode.OK, stagingEnv.StatusCode);
        var stagingToken = await PostJsonAsync<AgentTokenResponse>(
            client,
            "/api/agent-tokens",
            new { Name = "Staging agent", Environment = "staging" },
            HttpStatusCode.Created);

        await PostJsonAsync<ManualRunResponse>(
            client,
            $"/api/integrations/{integrationA.Id}/run",
            new { },
            HttpStatusCode.Accepted);

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Agent-Token", productionToken.Token);
        var poll = await GetJsonAsync<PollResponse>(client, "/api/agent/integrations");
        var work = Assert.Single(poll.Integrations);
        Assert.NotNull(work.WorkItemId);

        client.DefaultRequestHeaders.Remove("X-Agent-Token");
        client.DefaultRequestHeaders.Add("X-Agent-Token", stagingToken.Token);
        var stagingStart = await client.PostAsJsonAsync("/api/agent/executions", new { WorkItemId = work.WorkItemId.Value });
        Assert.Equal(HttpStatusCode.Conflict, stagingStart.StatusCode);

        var tenantB = await SeedTenantAndAgentTokenAsync(database, "tenant-b-agent-token");
        client.DefaultRequestHeaders.Remove("X-Agent-Token");
        client.DefaultRequestHeaders.Add("X-Agent-Token", tenantB.PlaintextToken);
        var tenantBStart = await client.PostAsJsonAsync("/api/agent/executions", new { WorkItemId = work.WorkItemId.Value });
        Assert.Equal(HttpStatusCode.NotFound, tenantBStart.StatusCode);

        client.DefaultRequestHeaders.Remove("X-Agent-Token");
        client.DefaultRequestHeaders.Add("X-Agent-Token", productionToken.Token);
        var started = await PostJsonAsync<StartExecutionResponse>(
            client,
            "/api/agent/executions",
            new { WorkItemId = work.WorkItemId.Value },
            HttpStatusCode.Created);

        client.DefaultRequestHeaders.Remove("X-Agent-Token");
        client.DefaultRequestHeaders.Add("X-Agent-Token", stagingToken.Token);
        var stagingComplete = await client.PutAsJsonAsync(
            $"/api/agent/executions/{started.ExecutionId}",
            new { Succeeded = true });
        Assert.Equal(HttpStatusCode.NotFound, stagingComplete.StatusCode);

        client.DefaultRequestHeaders.Remove("X-Agent-Token");
        client.DefaultRequestHeaders.Add("X-Agent-Token", secondProductionToken.Token);
        var wrongOwnerComplete = await client.PutAsJsonAsync(
            $"/api/agent/executions/{started.ExecutionId}",
            new { Succeeded = true });
        Assert.Equal(HttpStatusCode.NotFound, wrongOwnerComplete.StatusCode);

        client.DefaultRequestHeaders.Remove("X-Agent-Token");
        client.DefaultRequestHeaders.Add("X-Agent-Token", productionToken.Token);
        var ownerComplete = await client.PutAsJsonAsync(
            $"/api/agent/executions/{started.ExecutionId}",
            new { Succeeded = true });
        Assert.Equal(HttpStatusCode.NoContent, ownerComplete.StatusCode);
    }

    [Fact]
    public async Task AgentSecrets_MixedCaseEnvironmentInRoute_StillResolves()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var setup = await SetupTenantAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.Token);

        var setSecret = await client.PutAsJsonAsync("/api/secrets/production/API_KEY", new { value = "s3cr3t" });
        Assert.Equal(HttpStatusCode.OK, setSecret.StatusCode);

        var token = await PostJsonAsync<AgentTokenResponse>(
            client,
            "/api/agent-tokens",
            new { Name = "Production agent", Environment = "production" },
            HttpStatusCode.Created);

        // An agent configured as "Production" sends the mixed-case route. Stored environments are
        // lowercase, so the server must normalize before matching the token and loading secrets.
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Agent-Token", token.Token);
        using var secrets = await GetJsonDocumentAsync(client, "/api/agent/secrets/Production");

        // Embedded backend: the manifest carries an Inline entry with the decrypted value.
        var entry = secrets.RootElement.GetProperty("entries").EnumerateArray()
            .Single(e => e.GetProperty("key").GetString() == "API_KEY");
        Assert.Equal("Inline", entry.GetProperty("source").GetString());
        Assert.Equal("s3cr3t", entry.GetProperty("payload").GetString());
    }

    private static async Task<SetupResponse> SetupTenantAsync(HttpClient client)
    {
        return await PostJsonAsync<SetupResponse>(
            client,
            "/api/setup",
            new
            {
                TenantName = "Acme",
                TenantSlug = $"acme-{Guid.NewGuid():N}",
                AdminEmail = "admin@example.com",
                AdminPassword = "Password123!"
            });
    }

    private static async Task<SetupResponse> SeedTenantAndLoginAsync(IntegrationTestDatabase database, HttpClient client)
    {
        var tenant = await SeedTenantAsync(database, "Other Tenant", $"other-{Guid.NewGuid():N}", "other@example.com");
        var login = await PostJsonAsync<LoginResponse>(
            client,
            "/api/auth/login",
            new { Email = "other@example.com", Password = "Password123!" });

        return new SetupResponse(tenant.Id, login.Token);
    }

    private static async Task<AgentTokenSeed> SeedTenantAndAgentTokenAsync(
        IntegrationTestDatabase database,
        string tokenPlaintext)
    {
        var tenant = await SeedTenantAsync(database, "Agent Tenant", $"agent-{Guid.NewGuid():N}", "agent@example.com");
        await using var db = database.CreateContext();
        db.AgentTokens.Add(new AgentToken
        {
            TenantId = tenant.Id,
            Name = "Foreign tenant agent",
            Environment = "production",
            TokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(tokenPlaintext))).ToLowerInvariant()
        });
        await db.SaveChangesAsync();

        return new AgentTokenSeed(tokenPlaintext);
    }

    private static async Task<Tenant> SeedTenantAsync(
        IntegrationTestDatabase database,
        string name,
        string slug,
        string email)
    {
        await using var db = database.CreateContext();
        var tenant = new Tenant
        {
            Name = name,
            Slug = slug
        };
        db.Tenants.Add(tenant);
            TestEnvironments.Seed(db, tenant.Id, "production", "staging");
        db.Users.Add(new User
        {
            TenantId = tenant.Id,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
            Role = UserRole.Admin
        });
        await db.SaveChangesAsync();
        return tenant;
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

    private static async Task<T> GetJsonAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}. Body: {content}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
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

    private static void AssertNumberProperty(JsonElement element, string propertyName, int expected)
    {
        Assert.True(element.TryGetProperty(propertyName, out var property), $"Missing '{propertyName}'.");
        Assert.Equal(expected, property.GetInt32());
    }

    private static bool AssertBoolProperty(JsonElement element, string propertyName)
    {
        Assert.True(element.TryGetProperty(propertyName, out var property), $"Missing '{propertyName}'.");
        Assert.True(property.ValueKind is JsonValueKind.True or JsonValueKind.False);
        return property.GetBoolean();
    }

    private sealed record SetupResponse(Guid TenantId, string Token);
    private sealed record LoginResponse(string Token);
    private sealed record IntegrationResponse(Guid Id);
    private sealed record AgentTokenResponse(Guid Id, string Token);
    private sealed record ManualRunResponse(Guid RequestId);
    private sealed record PollResponse(IReadOnlyList<PollIntegrationResponse> Integrations);
    private sealed record PollIntegrationResponse(Guid Id, Guid? WorkItemId);
    private sealed record StartExecutionResponse(Guid ExecutionId);
    private sealed record AgentTokenSeed(string PlaintextToken);
}
