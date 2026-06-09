using System.Text.Json;
using System.Web;
using Shared.Domain;

namespace ControlPlane.Features.Alerts;

// Renders a FailedExecutionAlert into the subject/body for email and the JSON body for webhooks. Keeps
// the channel senders dumb — they only transmit what this produces.
public static class AlertMessageFormatter
{
    public static string Subject(FailedExecutionAlert alert) =>
        $"[Serto] Integration failed: {alert.IntegrationName} ({alert.Environment})";

    public static string TextBody(FailedExecutionAlert alert)
    {
        var lines = new List<string>
        {
            $"Integration '{alert.IntegrationName}' failed in {alert.Environment}.",
            "",
            $"Status:      {alert.Status}",
            $"Failed at:   {alert.FailedAt:u}",
            $"Attempt:     {alert.AttemptNumber}",
            $"Execution:   {alert.ExecutionId}"
        };

        if (!string.IsNullOrWhiteSpace(alert.PackageName))
            lines.Add($"Package:     {alert.PackageName} {alert.PackageVersion}");

        lines.Add("");
        lines.Add("Error:");
        lines.Add(string.IsNullOrWhiteSpace(alert.ErrorMessage) ? "(no error message reported)" : alert.ErrorMessage);

        return string.Join('\n', lines);
    }

    public static string HtmlBody(FailedExecutionAlert alert)
    {
        var package = string.IsNullOrWhiteSpace(alert.PackageName)
            ? ""
            : $"<tr><td><strong>Package</strong></td><td>{Encode(alert.PackageName)} {Encode(alert.PackageVersion)}</td></tr>";

        var error = string.IsNullOrWhiteSpace(alert.ErrorMessage)
            ? "(no error message reported)"
            : Encode(alert.ErrorMessage);

        return $"""
            <div style="font-family: -apple-system, Segoe UI, Roboto, sans-serif; color: #111;">
              <h2 style="margin: 0 0 12px;">Integration failed</h2>
              <p style="margin: 0 0 16px;">
                <strong>{Encode(alert.IntegrationName)}</strong> failed in <strong>{Encode(alert.Environment)}</strong>.
              </p>
              <table cellpadding="4" style="border-collapse: collapse;">
                <tr><td><strong>Status</strong></td><td>{alert.Status}</td></tr>
                <tr><td><strong>Failed at</strong></td><td>{alert.FailedAt:u}</td></tr>
                <tr><td><strong>Attempt</strong></td><td>{alert.AttemptNumber}</td></tr>
                <tr><td><strong>Execution</strong></td><td>{alert.ExecutionId}</td></tr>
                {package}
              </table>
              <p style="margin: 16px 0 4px;"><strong>Error</strong></p>
              <pre style="background: #f5f5f5; padding: 12px; border-radius: 6px; white-space: pre-wrap;">{error}</pre>
            </div>
            """;
    }

    public static string JsonPayload(FailedExecutionAlert alert) =>
        JsonSerializer.Serialize(new
        {
            type = "integration.failed",
            tenantId = alert.TenantId,
            integrationId = alert.IntegrationId,
            integrationName = alert.IntegrationName,
            environment = alert.Environment,
            executionId = alert.ExecutionId,
            status = alert.Status.ToString(),
            attemptNumber = alert.AttemptNumber,
            packageName = alert.PackageName,
            packageVersion = alert.PackageVersion,
            errorMessage = alert.ErrorMessage,
            failedAt = alert.FailedAt
        });

    private static string Encode(string? value) => HttpUtility.HtmlEncode(value ?? "");
}
