using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IO.Compression;
using ControlPlane.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shared.Domain;

namespace ControlPlane.Tests.IntegrationTests;

public class AgentExecutionApiIntegrationTests
{
    [Fact]
    public async Task ManualRun_CanBeClaimedStartedAndCompletedThroughApi()
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

        var integration = await PostJsonAsync<IntegrationResponse>(
            client,
            "/api/integrations",
            new
            {
                Name = "Sync Orders",
                Slug = $"sync-orders-{Guid.NewGuid():N}",
                Description = "Test integration",
                Environment = "production",
                TriggerType = "Scheduled",
                CronExpression = "0 * * * *",
                ClassName = "Tests.SyncOrdersIntegration"
            },
            HttpStatusCode.Created);

        var agentToken = await PostJsonAsync<AgentTokenResponse>(
            client,
            "/api/agent-tokens",
            new { Name = "Production agent", Environment = "production" },
            HttpStatusCode.Created);

        var manualRun = await PostJsonAsync<ManualRunResponse>(
            client,
            $"/api/integrations/{integration.Id}/run",
            new { },
            HttpStatusCode.Accepted);

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Agent-Token", agentToken.Token);

        var poll = await GetJsonAsync<PollResponse>(client, "/api/agent/integrations");
        var item = Assert.Single(poll.Integrations);

        Assert.Equal(integration.Id, item.Id);
        Assert.Equal("Manual", item.TriggerSource);
        Assert.Equal(manualRun.RequestId, item.ManualRunRequestId);
        Assert.NotNull(item.WorkItemId);

        var started = await PostJsonAsync<StartExecutionResponse>(
            client,
            "/api/agent/executions",
            new { WorkItemId = item.WorkItemId },
            HttpStatusCode.Created);

        var completeResponse = await client.PutAsJsonAsync(
            $"/api/agent/executions/{started.ExecutionId}",
            new { Succeeded = true, ErrorMessage = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, completeResponse.StatusCode);

        await using var db = database.CreateContext();
        var request = db.ManualRunRequests.Single(r => r.Id == manualRun.RequestId);
        var execution = db.ExecutionRecords.Single(e => e.Id == started.ExecutionId);

        Assert.Equal(ManualRunStatus.Started, request.Status);
        Assert.Equal(started.ExecutionId, request.ExecutionRecordId);
        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(TriggerSource.Manual, execution.TriggerSource);
        Assert.NotNull(execution.CompletedAt);
    }

    [Fact]
    public async Task AgentEndpoints_ReturnUnauthorizedWithoutAgentToken()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/agent/integrations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AgentPackageEndpoints_ListAndDownloadTenantPackagesWithAgentToken()
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

        var zip = CreateZipWithDll();
        using var form = new MultipartFormDataContent
        {
            { new StringContent("MyCompany.Integrations"), "Name" },
            { new StringContent("1.0.0"), "Version" },
            { new ByteArrayContent(zip), "File", "integrations.zip" }
        };

        var uploadResponse = await client.PostAsync("/api/integration-packages", form);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);

        var uploaded = (await uploadResponse.Content.ReadFromJsonAsync<PackageResponse>())!;

        var agentToken = await PostAgentTokenAsync(client, setup.Token);

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Agent-Token", agentToken.Token);

        var packages = await GetJsonAsync<AgentPackagesResponse>(client, "/api/agent/packages");
        var package = Assert.Single(packages.Packages);

        Assert.Equal(uploaded.Id, package.Id);
        Assert.Equal(uploaded.Name, package.Name);
        Assert.Equal(uploaded.Version, package.Version);
        Assert.Equal(uploaded.Sha256Hash, package.Sha256Hash);

        var download = await client.GetAsync($"/api/agent/packages/{uploaded.Id}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(zip, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task FailedExecution_WithRetryPolicy_QueuesRetryWorkItemForAgentPoll()
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

        var integration = await PostJsonAsync<IntegrationResponse>(
            client,
            "/api/integrations",
            new
            {
                Name = "Retry Job",
                Slug = $"retry-job-{Guid.NewGuid():N}",
                Environment = "production",
                TriggerType = "Manual",
                ClassName = "Tests.RetryJob",
                RetryMaxAttempts = 1,
                RetryBackoffSeconds = 0
            },
            HttpStatusCode.Created);

        var agentToken = await PostJsonAsync<AgentTokenResponse>(
            client,
            "/api/agent-tokens",
            new { Name = "Production agent", Environment = "production" },
            HttpStatusCode.Created);

        var manualRun = await PostJsonAsync<ManualRunResponse>(
            client,
            $"/api/integrations/{integration.Id}/run",
            new { },
            HttpStatusCode.Accepted);

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Agent-Token", agentToken.Token);

        var poll = await GetJsonAsync<PollResponse>(client, "/api/agent/integrations");
        var firstAttempt = Assert.Single(poll.Integrations);
        Assert.NotNull(firstAttempt.WorkItemId);

        var started = await PostJsonAsync<StartExecutionResponse>(
            client,
            "/api/agent/executions",
            new { WorkItemId = firstAttempt.WorkItemId.Value },
            HttpStatusCode.Created);

        var failed = await client.PutAsJsonAsync(
            $"/api/agent/executions/{started.ExecutionId}",
            new { Succeeded = false, ErrorMessage = "transient failure" });
        Assert.Equal(HttpStatusCode.NoContent, failed.StatusCode);

        var retryPoll = await GetJsonAsync<PollResponse>(client, "/api/agent/integrations");
        var retry = Assert.Single(retryPoll.Integrations);
        Assert.NotNull(retry.WorkItemId);

        Assert.Equal(integration.Id, retry.Id);
        Assert.Equal("Retry", retry.TriggerSource);
        Assert.NotEqual(firstAttempt.WorkItemId, retry.WorkItemId);

        await using var db = database.CreateContext();
        var retryWorkItem = db.WorkItems.Single(w => w.Id == retry.WorkItemId.Value);
        var firstExecution = db.ExecutionRecords.Single(e => e.Id == started.ExecutionId);

        Assert.Equal(TriggerSource.Retry, retryWorkItem.TriggerSource);
        Assert.Equal(2, retryWorkItem.AttemptNumber);
        Assert.Equal(started.ExecutionId, retryWorkItem.ParentExecutionId);
        Assert.Equal(started.ExecutionId, retryWorkItem.RootExecutionId);
        Assert.Equal(ExecutionStatus.Failed, firstExecution.Status);
        Assert.Equal(manualRun.RequestId, db.WorkItems.Single(w => w.Id == firstAttempt.WorkItemId.Value).ManualRunRequestId);
    }

    [Fact]
    public async Task AgentHeartbeat_CanBePostedAndListed()
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

        var agentToken = await PostJsonAsync<AgentTokenResponse>(
            client,
            "/api/agent-tokens",
            new { Name = "Production agent", Environment = "production" },
            HttpStatusCode.Created);

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Agent-Token", agentToken.Token);

        var heartbeat = await client.PostAsJsonAsync("/api/agent/heartbeat", new
        {
            Version = "1.2.3",
            Hostname = "worker-1",
            CurrentConcurrency = 2,
            MaxConcurrency = 5
        });
        Assert.Equal(HttpStatusCode.NoContent, heartbeat.StatusCode);

        client.DefaultRequestHeaders.Remove("X-Agent-Token");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.Token);

        var list = await GetJsonAsync<AgentHeartbeatListResponse>(client, "/api/agent-tokens/heartbeats");
        var item = Assert.Single(list.Agents);

        Assert.Equal(agentToken.Id, item.AgentTokenId);
        Assert.Equal("production", item.Environment);
        Assert.Equal("1.2.3", item.Version);
        Assert.Equal("worker-1", item.Hostname);
        Assert.Equal(2, item.CurrentConcurrency);
        Assert.Equal(5, item.MaxConcurrency);
        Assert.False(item.IsStale);
    }

    private static async Task<T> GetJsonAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
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
            $"Expected {expectedStatusCode}, got {response.StatusCode}. Body: {content}. WWW-Authenticate: {response.Headers.WwwAuthenticate}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<AgentTokenResponse> PostAgentTokenAsync(HttpClient client, string jwt)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return await PostJsonAsync<AgentTokenResponse>(
            client,
            "/api/agent-tokens",
            new { Name = "Production agent", Environment = "production" },
            HttpStatusCode.Created);
    }

    private static byte[] CreateZipWithDll()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("MyCompany.Integrations.dll");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("binary");
        }

        return stream.ToArray();
    }

    private sealed record SetupResponse(Guid TenantId, string Token);
    private sealed record IntegrationResponse(Guid Id);
    private sealed record AgentTokenResponse(Guid Id, string Token);
    private sealed record PackageResponse(Guid Id, string Name, string Version, string Sha256Hash);
    private sealed record AgentPackagesResponse(IReadOnlyList<AgentPackageResponse> Packages);
    private sealed record AgentPackageResponse(Guid Id, string Name, string Version, string Sha256Hash);
    private sealed record ManualRunResponse(Guid RequestId);
    private sealed record PollResponse(IReadOnlyList<PollIntegrationResponse> Integrations);
    private sealed record PollIntegrationResponse(
        Guid Id,
        string TriggerSource,
        Guid? ManualRunRequestId,
        Guid? WorkItemId);
    private sealed record StartExecutionResponse(Guid ExecutionId);
    private sealed record AgentHeartbeatListResponse(IReadOnlyList<AgentHeartbeatResponse> Agents);
    private sealed record AgentHeartbeatResponse(
        Guid AgentTokenId,
        string Environment,
        string? Version,
        string? Hostname,
        int CurrentConcurrency,
        int MaxConcurrency,
        bool IsStale);
}
