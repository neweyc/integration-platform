using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace ControlPlane.Tests.IntegrationTests;

public class WorkflowDagIntegrationTests
{
    [Fact]
    public async Task WorkflowRun_QueuesRootsAndReleasesDownstreamAfterSuccess()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var setup = await SetupAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.Token);

        var extract = await CreateIntegrationAsync(client, "extract");
        var transform = await CreateIntegrationAsync(client, "transform");
        var load = await CreateIntegrationAsync(client, "load");

        var workflow = await CreateWorkflowAsync(client, extract.Id, transform.Id, load.Id);
        var agentToken = await CreateAgentTokenAsync(client);

        var run = await PostJsonAsync<WorkflowRunResponse>(
            client,
            $"/api/workflows/{workflow.Id}/run",
            new { },
            HttpStatusCode.Accepted);

        Assert.Equal("Running", run.Status);
        Assert.Equal(3, run.Nodes.Count);
        Assert.Single(run.Nodes, n => n.NodeKey == "extract" && n.Status == "Queued");
        Assert.Equal(2, run.Nodes.Count(n => n.Status == "Pending"));

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Agent-Token", agentToken.Token);

        var firstPoll = await GetJsonAsync<PollResponse>(client, "/api/agent/integrations");
        var first = Assert.Single(firstPoll.Integrations);
        Assert.Equal(extract.Id, first.Id);
        Assert.Equal("Workflow", first.TriggerSource);
        Assert.NotNull(first.WorkItemId);

        await StartAndCompleteAsync(client, first.WorkItemId!.Value, succeeded: true);

        var secondPoll = await GetJsonAsync<PollResponse>(client, "/api/agent/integrations");
        var second = Assert.Single(secondPoll.Integrations);
        Assert.Equal(transform.Id, second.Id);

        await StartAndCompleteAsync(client, second.WorkItemId!.Value, succeeded: true);

        var thirdPoll = await GetJsonAsync<PollResponse>(client, "/api/agent/integrations");
        var third = Assert.Single(thirdPoll.Integrations);
        Assert.Equal(load.Id, third.Id);

        await StartAndCompleteAsync(client, third.WorkItemId!.Value, succeeded: true);

        await using var db = database.CreateContext();
        var workflowRun = await db.WorkflowRuns
            .Include(r => r.NodeRuns)
            .SingleAsync(r => r.Id == run.Id);

        Assert.Equal(WorkflowRunStatus.Succeeded, workflowRun.Status);
        Assert.All(workflowRun.NodeRuns, n => Assert.Equal(WorkflowNodeRunStatus.Succeeded, n.Status));
        Assert.Equal(3, db.ExecutionRecords.Count(e => e.TriggerSource == TriggerSource.Workflow));
    }

    [Fact]
    public async Task Workflows_CanBeListedForTenant()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var setup = await SetupAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.Token);

        var extract = await CreateIntegrationAsync(client, "list-extract");
        var load = await CreateIntegrationAsync(client, "list-load");
        var workflow = await CreateWorkflowAsync(client, extract.Id, load.Id);

        var list = await GetJsonAsync<ListWorkflowsResponse>(client, "/api/workflows");

        var listed = Assert.Single(list.Workflows, w => w.Id == workflow.Id);
        Assert.Equal("production", listed.Environment);
        Assert.Equal(2, listed.Nodes?.Count);
        Assert.Single(listed.Edges!);
    }


    [Fact]
    public async Task WorkflowRun_FanInWaitsForAllParents()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var setup = await SetupAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.Token);

        var a = await CreateIntegrationAsync(client, "extract-a");
        var b = await CreateIntegrationAsync(client, "extract-b");
        var join = await CreateIntegrationAsync(client, "join");

        var workflow = await PostJsonAsync<WorkflowDefinitionResponse>(
            client,
            "/api/workflows",
            new
            {
                Name = "Fan In",
                Slug = $"fan-in-{Guid.NewGuid():N}",
                Environment = "production",
                Nodes = new[]
                {
                    new { Key = "a", Name = "Extract A", IntegrationId = a.Id },
                    new { Key = "b", Name = "Extract B", IntegrationId = b.Id },
                    new { Key = "join", Name = "Join", IntegrationId = join.Id }
                },
                Edges = new[]
                {
                    new { From = "a", To = "join" },
                    new { From = "b", To = "join" }
                }
            },
            HttpStatusCode.Created);
        var agentToken = await CreateAgentTokenAsync(client);
        await PostJsonAsync<WorkflowRunResponse>(client, $"/api/workflows/{workflow.Id}/run", new { }, HttpStatusCode.Accepted);

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Agent-Token", agentToken.Token);

        var firstPoll = await GetJsonAsync<PollResponse>(client, "/api/agent/integrations");
        Assert.Equal(2, firstPoll.Integrations.Count);

        await StartAndCompleteAsync(client, firstPoll.Integrations[0].WorkItemId!.Value, succeeded: true);

        await using (var db = database.CreateContext())
        {
            Assert.Equal(0, db.WorkItems.Count(w => w.IntegrationId == join.Id && w.TriggerSource == TriggerSource.Workflow));
        }

        await StartAndCompleteAsync(client, firstPoll.Integrations[1].WorkItemId!.Value, succeeded: true);

        var joinPoll = await GetJsonAsync<PollResponse>(client, "/api/agent/integrations");
        var joinItem = Assert.Single(joinPoll.Integrations);
        Assert.Equal(join.Id, joinItem.Id);
    }

    [Fact]
    public async Task WorkflowRun_FailedNodeBlocksDownstreamAndFailsRun()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var setup = await SetupAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.Token);

        var extract = await CreateIntegrationAsync(client, "extract-fail");
        var load = await CreateIntegrationAsync(client, "load-blocked");
        var workflow = await CreateWorkflowAsync(client, extract.Id, load.Id);
        var agentToken = await CreateAgentTokenAsync(client);
        var run = await PostJsonAsync<WorkflowRunResponse>(client, $"/api/workflows/{workflow.Id}/run", new { }, HttpStatusCode.Accepted);

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Agent-Token", agentToken.Token);

        var poll = await GetJsonAsync<PollResponse>(client, "/api/agent/integrations");
        var item = Assert.Single(poll.Integrations);
        await StartAndCompleteAsync(client, item.WorkItemId!.Value, succeeded: false);

        var nextPoll = await GetJsonAsync<PollResponse>(client, "/api/agent/integrations");
        Assert.Empty(nextPoll.Integrations);

        await using var db = database.CreateContext();
        var workflowRun = await db.WorkflowRuns
            .Include(r => r.NodeRuns)
            .SingleAsync(r => r.Id == run.Id);

        Assert.Equal(WorkflowRunStatus.Failed, workflowRun.Status);
        Assert.Contains(workflowRun.NodeRuns, n => n.Status == WorkflowNodeRunStatus.Failed);
        Assert.Contains(workflowRun.NodeRuns, n => n.Status == WorkflowNodeRunStatus.Pending);
    }

    [Fact]
    public async Task WorkflowRun_FailureHaltsParallelBranchDownstreamDispatch()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var setup = await SetupAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.Token);

        // Two independent branches: "a" (will fail) and "b" -> "c" (would otherwise continue).
        var a = await CreateIntegrationAsync(client, "branch-a");
        var b = await CreateIntegrationAsync(client, "branch-b");
        var c = await CreateIntegrationAsync(client, "branch-c");

        var workflow = await PostJsonAsync<WorkflowDefinitionResponse>(
            client,
            "/api/workflows",
            new
            {
                Name = "Parallel Branches",
                Slug = $"parallel-{Guid.NewGuid():N}",
                Environment = "production",
                Nodes = new[]
                {
                    new { Key = "a", Name = "A", IntegrationId = a.Id },
                    new { Key = "b", Name = "B", IntegrationId = b.Id },
                    new { Key = "c", Name = "C", IntegrationId = c.Id }
                },
                Edges = new[]
                {
                    new { From = "b", To = "c" }
                }
            },
            HttpStatusCode.Created);

        var agentToken = await CreateAgentTokenAsync(client);
        var run = await PostJsonAsync<WorkflowRunResponse>(
            client, $"/api/workflows/{workflow.Id}/run", new { }, HttpStatusCode.Accepted);

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Agent-Token", agentToken.Token);

        // Roots a and b are queued together.
        var poll = await GetJsonAsync<PollResponse>(client, "/api/agent/integrations");
        Assert.Equal(2, poll.Integrations.Count);
        var failItem = poll.Integrations.Single(i => i.Id == a.Id);
        var okItem = poll.Integrations.Single(i => i.Id == b.Id);

        // Branch a fails — the run becomes Failed.
        await StartAndCompleteAsync(client, failItem.WorkItemId!.Value, succeeded: false);

        // Branch b then succeeds. Its downstream "c" must NOT be dispatched now the run has failed.
        await StartAndCompleteAsync(client, okItem.WorkItemId!.Value, succeeded: true);

        // Nothing further should be queued for the agent.
        var nextPoll = await GetJsonAsync<PollResponse>(client, "/api/agent/integrations");
        Assert.Empty(nextPoll.Integrations);

        await using var db = database.CreateContext();

        // No work item was ever created for c.
        Assert.Equal(0, db.WorkItems.Count(w => w.IntegrationId == c.Id && w.TriggerSource == TriggerSource.Workflow));

        var workflowRun = await db.WorkflowRuns
            .Include(r => r.NodeRuns).ThenInclude(n => n.WorkflowNode)
            .SingleAsync(r => r.Id == run.Id);

        Assert.Equal(WorkflowRunStatus.Failed, workflowRun.Status);
        // c never ran — it stays Pending, not Queued/Running.
        var cNodeRun = workflowRun.NodeRuns.Single(n => n.WorkflowNode.Key == "c");
        Assert.Equal(WorkflowNodeRunStatus.Pending, cNodeRun.Status);
    }

    private static async Task<SetupResponse> SetupAsync(HttpClient client) =>
        await PostJsonAsync<SetupResponse>(
            client,
            "/api/setup",
            new
            {
                TenantName = "Acme",
                TenantSlug = $"acme-{Guid.NewGuid():N}",
                AdminEmail = "admin@example.com",
                AdminPassword = "Password123!"
            });

    private static async Task<IntegrationResponse> CreateIntegrationAsync(HttpClient client, string slugPrefix) =>
        await PostJsonAsync<IntegrationResponse>(
            client,
            "/api/integrations",
            new
            {
                Name = slugPrefix,
                Slug = $"{slugPrefix}-{Guid.NewGuid():N}",
                Description = "Workflow test integration",
                Environment = "production",
                TriggerType = "Manual",
                ClassName = $"Tests.{slugPrefix.Replace("-", "")}"
            },
            HttpStatusCode.Created);

    private static async Task<WorkflowDefinitionResponse> CreateWorkflowAsync(
        HttpClient client,
        Guid firstIntegrationId,
        Guid secondIntegrationId,
        Guid? thirdIntegrationId = null)
    {
        object[] nodes = thirdIntegrationId.HasValue
            ? [
                new { Key = "extract", Name = "Extract", IntegrationId = firstIntegrationId },
                new { Key = "transform", Name = "Transform", IntegrationId = secondIntegrationId },
                new { Key = "load", Name = "Load", IntegrationId = thirdIntegrationId.Value }
            ]
            : [
                new { Key = "extract", Name = "Extract", IntegrationId = firstIntegrationId },
                new { Key = "load", Name = "Load", IntegrationId = secondIntegrationId }
            ];

        object[] edges = thirdIntegrationId.HasValue
            ? [
                new { From = "extract", To = "transform" },
                new { From = "transform", To = "load" }
            ]
            : [
                new { From = "extract", To = "load" }
            ];

        return await PostJsonAsync<WorkflowDefinitionResponse>(
            client,
            "/api/workflows",
            new
            {
                Name = "Order Workflow",
                Slug = $"order-workflow-{Guid.NewGuid():N}",
                Environment = "production",
                Nodes = nodes,
                Edges = edges
            },
            HttpStatusCode.Created);
    }

    private static async Task<AgentTokenResponse> CreateAgentTokenAsync(HttpClient client) =>
        await PostJsonAsync<AgentTokenResponse>(
            client,
            "/api/agent-tokens",
            new { Name = "Production agent", Environment = "production" },
            HttpStatusCode.Created);

    private static async Task StartAndCompleteAsync(HttpClient client, Guid workItemId, bool succeeded)
    {
        var started = await PostJsonAsync<StartExecutionResponse>(
            client,
            "/api/agent/executions",
            new { WorkItemId = workItemId },
            HttpStatusCode.Created);

        var complete = await client.PutAsJsonAsync(
            $"/api/agent/executions/{started.ExecutionId}",
            new { Succeeded = succeeded, ErrorMessage = succeeded ? null : "failed", Retryable = false });
        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
    }

    private static async Task<T> GetJsonAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {url} failed: {response.StatusCode}. Body: {content}");
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
        Assert.Equal(expectedStatusCode, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed record SetupResponse(Guid TenantId, string Token);
    private sealed record IntegrationResponse(Guid Id);
    private sealed record AgentTokenResponse(Guid Id, string Token);
    private sealed record WorkflowDefinitionResponse(
        Guid Id,
        string? Environment = null,
        IReadOnlyList<WorkflowNodeResponse>? Nodes = null,
        IReadOnlyList<WorkflowEdgeResponse>? Edges = null);
    private sealed record ListWorkflowsResponse(IReadOnlyList<WorkflowDefinitionResponse> Workflows);
    private sealed record WorkflowNodeResponse(Guid Id, string Key, Guid IntegrationId);
    private sealed record WorkflowEdgeResponse(string From, string To);
    private sealed record WorkflowRunResponse(Guid Id, string Status, IReadOnlyList<WorkflowNodeRunResponse> Nodes);
    private sealed record WorkflowNodeRunResponse(string NodeKey, string Status);
    private sealed record PollResponse(IReadOnlyList<PollIntegrationResponse> Integrations);
    private sealed record PollIntegrationResponse(Guid Id, string TriggerSource, Guid? WorkItemId);
    private sealed record StartExecutionResponse(Guid ExecutionId);
}
