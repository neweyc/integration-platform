using System.Net.Http.Json;
using System.Text.Json;

namespace RuntimeAgent.Agent;

public interface IControlPlaneClient
{
    Task<List<IntegrationItem>> GetIntegrationsAsync(CancellationToken ct);
    Task<Dictionary<string, string>> GetSecretsAsync(CancellationToken ct);
    Task<Guid> StartExecutionAsync(Guid integrationId, string triggerSource, Guid? manualRunRequestId, CancellationToken ct);
    Task RecordLogAsync(Guid executionId, ExecutionLogEntry log, CancellationToken ct);
    Task CompleteExecutionAsync(Guid executionId, bool succeeded, string? errorMessage, CancellationToken ct);
}

public record IntegrationItem(
    Guid Id,
    string Name,
    string Slug,
    string TriggerType,
    string? CronExpression,
    string ClassName,
    DateTime? LeaseExpiresAt,
    string TriggerSource,
    Guid? ManualRunRequestId);

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

    public async Task<Guid> StartExecutionAsync(Guid integrationId, string triggerSource, Guid? manualRunRequestId, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync(
            "/api/agent/executions",
            new { IntegrationId = integrationId, TriggerSource = triggerSource, ManualRunRequestId = manualRunRequestId },
            JsonOptions, ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<StartExecutionResponse>(JsonOptions, ct);
        return result!.ExecutionId;
    }

    public async Task CompleteExecutionAsync(Guid executionId, bool succeeded, string? errorMessage, CancellationToken ct)
    {
        var response = await http.PutAsJsonAsync(
            $"/api/agent/executions/{executionId}",
            new { Succeeded = succeeded, ErrorMessage = errorMessage },
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
    private record StartExecutionResponse(Guid ExecutionId, DateTime StartedAt);
}
