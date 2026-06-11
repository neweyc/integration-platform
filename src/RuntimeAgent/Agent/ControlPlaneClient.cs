using System.Net.Http.Json;
using System.Text.Json;

namespace RuntimeAgent.Agent;

public interface IControlPlaneClient
{
    Task<List<IntegrationItem>> GetIntegrationsAsync(CancellationToken ct);
    Task<Dictionary<string, string>> GetSecretsAsync(CancellationToken ct);
    Task<List<AgentPackageInfo>> ListPackagesAsync(CancellationToken ct);
    Task<byte[]> DownloadPackageAsync(Guid packageId, CancellationToken ct);
    Task SendHeartbeatAsync(AgentHeartbeatInfo heartbeat, CancellationToken ct);
    Task<Guid> StartExecutionAsync(Guid workItemId, CancellationToken ct);
    Task RecordLogAsync(Guid executionId, ExecutionLogEntry log, CancellationToken ct);
    Task CompleteExecutionAsync(Guid executionId, bool succeeded, string? errorMessage, CancellationToken ct, bool isTimeout = false, bool retryable = true);
    Task PublishMessageAsync(string subject, string? body, Guid? sourceExecutionId, CancellationToken ct);
}

public record AgentPackageInfo(Guid Id, string Name, string Version, string Sha256Hash);
public record AgentHeartbeatInfo(string? Version, string? Hostname, int CurrentConcurrency, int MaxConcurrency);

public record IntegrationItem(
    Guid Id,
    string Name,
    string Slug,
    string TriggerType,
    string? CronExpression,
    string ClassName,
    DateTime? LeaseExpiresAt,
    string TriggerSource,
    Guid? ManualRunRequestId,
    int? TimeoutSeconds = null,
    Guid? WorkItemId = null,
    Guid? PackageId = null,
    string? Payload = null,
    string? MessageSubject = null,            // Queue
    Guid? MessageId = null,                   // Queue
    DateTime? MessagePublishedAt = null,      // Queue
    Guid? ParentExecutionId = null,           // Queue (message source) / Workflow (upstream)
    string? DeliveryId = null,                // Webhook
    Guid? WorkflowRunId = null,               // Workflow
    Guid? WorkflowNodeId = null,              // Workflow
    int AttemptNumber = 1);                   // Retry

public record ExecutionLogEntry(
    DateTime Timestamp,
    string Level,
    string Message,
    string? Exception,
    string? PropertiesJson);

public class ControlPlaneClient(HttpClient http, AgentOptions options, IVaultClient vault, ILogger<ControlPlaneClient> logger)
    : IControlPlaneClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<IntegrationItem>> GetIntegrationsAsync(CancellationToken ct)
    {
        var response = await http.GetFromJsonAsync<IntegrationsResponse>(
            "/api/agent/integrations", JsonOptions, ct);

        return response?.Integrations ?? [];
    }

    public async Task<Dictionary<string, string>> GetSecretsAsync(CancellationToken ct)
    {
        var response = await http.GetFromJsonAsync<SecretManifestResponse>(
            $"/api/agent/secrets/{options.Environment}", JsonOptions, ct);

        var entries = response?.Entries ?? [];
        var secrets = new Dictionary<string, string>(entries.Count);

        foreach (var entry in entries)
        {
            // Inline = the control plane already holds the value (embedded backend). Reference = the value
            // lives in the vault on this network and we resolve it here, keeping it off the control plane.
            secrets[entry.Key] = entry.Source == SecretSource.Reference
                ? await vault.ResolveAsync(entry.Payload, ct)
                : entry.Payload;
        }

        return secrets;
    }

    public async Task<List<AgentPackageInfo>> ListPackagesAsync(CancellationToken ct)
    {
        var response = await http.GetFromJsonAsync<AgentPackagesResponse>(
            "/api/agent/packages", JsonOptions, ct);

        return response?.Packages ?? [];
    }

    public async Task<byte[]> DownloadPackageAsync(Guid packageId, CancellationToken ct)
    {
        var response = await http.GetAsync($"/api/agent/packages/{packageId}/download", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task SendHeartbeatAsync(AgentHeartbeatInfo heartbeat, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync(
            "/api/agent/heartbeat",
            heartbeat,
            JsonOptions,
            ct);

        if (!response.IsSuccessStatusCode)
            logger.LogDebug("Failed to send heartbeat: {Status}", response.StatusCode);
    }

    public async Task<Guid> StartExecutionAsync(Guid workItemId, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync(
            "/api/agent/executions",
            new { WorkItemId = workItemId },
            JsonOptions, ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<StartExecutionResponse>(JsonOptions, ct);
        return result!.ExecutionId;
    }

    public async Task CompleteExecutionAsync(Guid executionId, bool succeeded, string? errorMessage, CancellationToken ct, bool isTimeout = false, bool retryable = true)
    {
        var response = await http.PutAsJsonAsync(
            $"/api/agent/executions/{executionId}",
            new { Succeeded = succeeded, ErrorMessage = errorMessage, IsTimeout = isTimeout, Retryable = retryable },
            JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
            logger.LogWarning("Failed to complete execution {ExecutionId}: {Status}", executionId, response.StatusCode);
    }

    public async Task PublishMessageAsync(string subject, string? body, Guid? sourceExecutionId, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync(
            "/api/agent/messages",
            new { Subject = subject, Body = body, SourceExecutionId = sourceExecutionId },
            JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
            logger.LogWarning("Failed to publish message '{Subject}': {Status}", subject, response.StatusCode);
    }

    public async Task RecordLogAsync(Guid executionId, ExecutionLogEntry log, CancellationToken ct)
    {
        try
        {
            var response = await http.PostAsJsonAsync(
                $"/api/agent/executions/{executionId}/logs",
                log,
                JsonOptions,
                ct);

            if (!response.IsSuccessStatusCode)
                logger.LogDebug(
                    "Failed to record log for execution {ExecutionId}: {Status}",
                    executionId,
                    response.StatusCode);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Failed to record log for execution {ExecutionId}", executionId);
        }
    }

    private record IntegrationsResponse(List<IntegrationItem> Integrations);
    private record SecretManifestResponse(List<SecretManifestEntry> Entries);
    private record AgentPackagesResponse(List<AgentPackageInfo> Packages);
    private record StartExecutionResponse(Guid ExecutionId, DateTime StartedAt);
}
