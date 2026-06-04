using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Shared.Domain;

namespace ControlPlane.Tests.IntegrationTests;

public class WebhookApiIntegrationTests
{
    [Fact]
    public async Task Webhook_FullFlow_DeliversVerifiesQueuesAndAgentPollsWithPayload()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var setup = await PostJsonAsync<SetupResponse>(client, "/api/setup", new
        {
            TenantName = "Acme",
            TenantSlug = $"acme-{Guid.NewGuid():N}",
            AdminEmail = "admin@example.com",
            AdminPassword = "Password123!"
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.Token);

        // Response carries the one-time secret, URL, and signing scheme.
        var integration = await PostJsonAsync<IntegrationResponse>(client, "/api/integrations", new
        {
            Name = "Incoming Orders",
            Slug = $"incoming-orders-{Guid.NewGuid():N}",
            Environment = "production",
            Triggers = new[] { new { Name = "Webhook", Slug = "webhook", Type = "Webhook" } },
            ClassName = "Acme.IncomingOrders"
        }, HttpStatusCode.Created);

        Assert.NotNull(integration.WebhookSecret);
        Assert.StartsWith("whs_", integration.WebhookSecret);
        Assert.NotNull(integration.WebhookUrl);
        Assert.NotNull(integration.WebhookSigning);
        Assert.Equal("HMAC-SHA256", integration.WebhookSigning!.Algorithm);
        Assert.Equal("X-Integration-Signature", integration.WebhookSigning.SignatureHeader);
        Assert.Equal("X-Integration-Timestamp", integration.WebhookSigning.TimestampHeader);
        Assert.Equal(300, integration.WebhookSigning.ToleranceSeconds);

        var agentToken = await PostJsonAsync<AgentTokenResponse>(client, "/api/agent-tokens",
            new { Name = "Production agent", Environment = "production" }, HttpStatusCode.Created);

        // Deliver a webhook with a valid signature
        client.DefaultRequestHeaders.Authorization = null;
        var body = """{"orderId":42,"total":19.99}""";
        var deliveryId = Guid.NewGuid().ToString();

        var firstDelivery = await PostWebhookAsync(
            client, integration.WebhookUrl!, body, integration.WebhookSecret!, deliveryId);
        Assert.Equal(HttpStatusCode.Accepted, firstDelivery.StatusCode);

        // Agent polls and sees the webhook work item with the raw payload
        client.DefaultRequestHeaders.Add("X-Agent-Token", agentToken.Token);
        var poll = await GetJsonAsync<PollResponse>(client, "/api/agent/integrations");
        var item = Assert.Single(poll.Integrations);
        Assert.Equal(integration.Id, item.Id);
        Assert.Equal("Webhook", item.TriggerSource);
        Assert.Equal(body, item.Payload);
        Assert.NotNull(item.WorkItemId);

        // Verify the work item persisted with the delivery id
        await using var db = database.CreateContext();
        var workItem = db.WorkItems.Single(w => w.IntegrationId == integration.Id);
        Assert.Equal(TriggerSource.Webhook, workItem.TriggerSource);
        Assert.Equal(deliveryId, workItem.DeliveryId);
        Assert.Equal(body, workItem.Payload);
    }

    [Fact]
    public async Task Webhook_DuplicateDeliveryId_IsIdempotent()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var (_, integration) = await SetupWebhookIntegrationAsync(client);
        client.DefaultRequestHeaders.Authorization = null;

        var body = """{"x":1}""";
        var deliveryId = Guid.NewGuid().ToString();

        var first = await PostWebhookAsync(client, integration.WebhookUrl!, body, integration.WebhookSecret!, deliveryId);
        var second = await PostWebhookAsync(client, integration.WebhookUrl!, body, integration.WebhookSecret!, deliveryId);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await using var db = database.CreateContext();
        Assert.Equal(1, db.WorkItems.Count(w => w.IntegrationId == integration.Id));
    }

    [Fact]
    public async Task Webhook_SameDeliveryIdAcrossDifferentIntegrations_QueuesBoth()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var (_, first) = await SetupWebhookIntegrationAsync(client);
        var second = await PostJsonAsync<IntegrationResponse>(client, "/api/integrations", new
        {
            Name = "Second Hook",
            Slug = $"second-hook-{Guid.NewGuid():N}",
            Environment = "production",
            Triggers = new[] { new { Name = "Webhook", Slug = "webhook", Type = "Webhook" } },
            ClassName = "Acme.SecondHook"
        }, HttpStatusCode.Created);
        client.DefaultRequestHeaders.Authorization = null;

        var deliveryId = Guid.NewGuid().ToString();
        var firstResponse = await PostWebhookAsync(client, first.WebhookUrl!, """{"source":"first"}""",
            first.WebhookSecret!, deliveryId);
        var secondResponse = await PostWebhookAsync(client, second.WebhookUrl!, """{"source":"second"}""",
            second.WebhookSecret!, deliveryId);

        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);

        await using var db = database.CreateContext();
        Assert.Equal(1, db.WorkItems.Count(w => w.IntegrationId == first.Id && w.DeliveryId == deliveryId));
        Assert.Equal(1, db.WorkItems.Count(w => w.IntegrationId == second.Id && w.DeliveryId == deliveryId));
    }

    [Fact]
    public async Task Webhook_BadSignature_Returns401AndQueuesNothing()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var (_, integration) = await SetupWebhookIntegrationAsync(client);
        client.DefaultRequestHeaders.Authorization = null;

        using var content = new StringContent("""{"x":1}""", Encoding.UTF8);
        content.Headers.Add("X-Integration-Signature", "sha256=deadbeef");
        var response = await client.PostAsync(integration.WebhookUrl, content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await using var db = database.CreateContext();
        Assert.Equal(0, db.WorkItems.Count(w => w.IntegrationId == integration.Id));
    }

    [Fact]
    public async Task Webhook_StaleTimestamp_Returns401AndQueuesNothing()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var (_, integration) = await SetupWebhookIntegrationAsync(client);
        client.DefaultRequestHeaders.Authorization = null;

        // Authentic signature over a 10-minute-old timestamp — a captured replay.
        var body = "{}";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var staleTs = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600).ToString();
        var signedPayload = Encoding.UTF8.GetBytes($"{staleTs}.").Concat(bodyBytes).ToArray();
        var sig = "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(integration.WebhookSecret!), signedPayload)).ToLowerInvariant();

        using var content = new ByteArrayContent(bodyBytes);
        content.Headers.Add("X-Integration-Signature", sig);
        content.Headers.Add("X-Integration-Timestamp", staleTs);
        var response = await client.PostAsync(integration.WebhookUrl, content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await using var db = database.CreateContext();
        Assert.Equal(0, db.WorkItems.Count(w => w.IntegrationId == integration.Id));
        // The rejection is recorded as an Expired delivery for operator visibility.
        Assert.Equal(1, db.WebhookDeliveries.Count(d =>
            d.IntegrationId == integration.Id && d.Outcome == WebhookDeliveryOutcome.Expired));
    }

    [Fact]
    public async Task Webhook_UnknownIntegration_Returns404()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var tenantSlug = $"acme-{Guid.NewGuid():N}";
        await PostJsonAsync<SetupResponse>(client, "/api/setup", new
        {
            TenantName = "Acme",
            TenantSlug = tenantSlug,
            AdminEmail = "admin@example.com",
            AdminPassword = "Password123!"
        });

        using var content = new StringContent("{}", Encoding.UTF8);
        content.Headers.Add("X-Integration-Signature", "sha256=x");
        var response = await client.PostAsync($"/webhooks/{tenantSlug}/ghost-integration/default", content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<(SetupResponse Setup, IntegrationResponse Integration)> SetupWebhookIntegrationAsync(HttpClient client)
    {
        var setup = await PostJsonAsync<SetupResponse>(client, "/api/setup", new
        {
            TenantName = "Acme",
            TenantSlug = $"acme-{Guid.NewGuid():N}",
            AdminEmail = "admin@example.com",
            AdminPassword = "Password123!"
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.Token);

        var integration = await PostJsonAsync<IntegrationResponse>(client, "/api/integrations", new
        {
            Name = "Hook",
            Slug = $"hook-{Guid.NewGuid():N}",
            Environment = "production",
            Triggers = new[] { new { Name = "Webhook", Slug = "webhook", Type = "Webhook" } },
            ClassName = "Acme.Hook"
        }, HttpStatusCode.Created);

        return (setup, integration);
    }

    private static async Task<HttpResponseMessage> PostWebhookAsync(
        HttpClient client, string url, string body, string secret, string deliveryId)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        // Replay-protected scheme: sign over "{timestamp}.{body}".
        var signedPayload = Encoding.UTF8.GetBytes($"{timestamp}.").Concat(bodyBytes).ToArray();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signedPayload);
        var signature = "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();

        using var content = new ByteArrayContent(bodyBytes);
        content.Headers.Add("X-Integration-Signature", signature);
        content.Headers.Add("X-Integration-Timestamp", timestamp);
        content.Headers.Add("X-Integration-Delivery", deliveryId);

        return await client.PostAsync(url, content);
    }

    private static async Task<T> GetJsonAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<T> PostJsonAsync<T>(
        HttpClient client, string url, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.PostAsJsonAsync(url, body);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,
            $"Expected {expected}, got {response.StatusCode}. Body: {content}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed record SetupResponse(Guid TenantId, string Token);
    private sealed record IntegrationResponse(Guid Id, IReadOnlyList<TriggerResponse> Triggers)
    {
        private TriggerResponse Webhook => Triggers.Single(t => t.Type == "Webhook");
        public string? WebhookSecret => Webhook.WebhookSecret;
        public string? WebhookUrl => Webhook.WebhookUrl;
        public WebhookSigningResponse? WebhookSigning => Webhook.WebhookSigning;
    }
    private sealed record TriggerResponse(
        Guid Id, string Name, string Slug, string Type, bool Enabled, string? WebhookSecret,
        string? WebhookUrl, WebhookSigningResponse? WebhookSigning);
    private sealed record WebhookSigningResponse(
        string Algorithm, string SignatureHeader, string SignatureFormat, string DeliveryIdHeader,
        string TimestampHeader, int ToleranceSeconds);
    private sealed record AgentTokenResponse(Guid Id, string Token);
    private sealed record PollResponse(IReadOnlyList<PollIntegrationResponse> Integrations);
    private sealed record PollIntegrationResponse(
        Guid Id, string TriggerSource, Guid? WorkItemId, string? Payload);
}
