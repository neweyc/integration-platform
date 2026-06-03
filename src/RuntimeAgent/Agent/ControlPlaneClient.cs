using System.Net.Http.Json;
using System.Text.Json;

namespace RuntimeAgent.Agent;

public interface IControlPlaneClient
{
    Task<List<IntegrationItem>> GetIntegrationsAsync(CancellationToken ct);
    Task<Dictionary<string, string>> GetSecretsAsync(CancellationToken ct);
    Task<List<AgentPackageInfo>> ListPackagesAsync(CancellationToken ct);
    Task<byte[]> DownloadPackageAsync(Guid packageId, CancellationToken ct);
    Task<Guid> StartExecutionAsync(Guid workItemId, CancellationToken ct);
    Task RecordLogAsync(Guid executionId, ExecutionLogEntry log, CancellationToken ct);
    Task CompleteExecutionAsync(Guid executionId, bool succeeded, string? errorMessage, CancellationToken ct, bool isTimeout = false);
}

public record AgentPackageInfo(Guid Id, string Name, string Version, string Sha256Hash);

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
    Guid? PackageId = null);

public record ExecutionLogEntry(
    DateTime Timestamp,
    string Level,
    string Message,
    string? Exception,
    string? PropertiesJson);

public class ControlPlaneClient(HttpClient http, AgentOptions options, ILogger<ControlPlaneClient> logger)
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
        var response = await http.GetFromJsonAsync<SecretsResponse>(
            $"/api/agent/secrets/{options.Environment}", JsonOptions, ct);

        return response?.Secrets ?? [];
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

    public async Task CompleteExecutionAsync(Guid executionId, bool succeeded, string? errorMessage, CancellationToken ct, bool isTimeout = false)
    {
        var response = await http.PutAsJsonAsync(
            $"/api/agent/executions/{executionId}",
            new { Succeeded = succeeded, ErrorMessage = errorMessage, IsTimeout = isTimeout },
            JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
            logger.LogWarning("Failed to complete execution {ExecutionId}: {Status}", executionId, response.StatusCode);
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
    private record SecretsResponse(Dictionary<string, string> Secrets);
    private record AgentPackagesResponse(List<AgentPackageInfo> Packages);
    private record StartExecutionResponse(Guid ExecutionId, DateTime StartedAt);
}
