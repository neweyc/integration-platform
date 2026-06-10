using System.Text.Json;
using System.Web;

namespace ControlPlane.Features.Alerts;

// Renders an UnroutableIntegrationAlert into email subject/body and a webhook JSON payload, mirroring
// AlertMessageFormatter so the channel senders stay dumb.
public static class UnroutableAlertFormatter
{
    public static string Subject(UnroutableIntegrationAlert alert) =>
        $"[Serto] Integration can't be routed: {alert.IntegrationName} ({alert.Environment})";

    public static string TextBody(UnroutableIntegrationAlert alert)
    {
        var tags = string.Join(", ", alert.RequiredTags);
        return string.Join('\n',
            $"Integration '{alert.IntegrationName}' can't run in {alert.Environment}.",
            "",
            $"No live agent in that environment offers the capabilities it requires, so its work will",
            $"queue until a matching agent connects.",
            "",
            $"Required capabilities: {tags}",
            $"Detected at:           {alert.DetectedAt:u}",
            "",
            "Start (or fix) an agent in this environment whose configured Tags include all of the above.");
    }

    public static string HtmlBody(UnroutableIntegrationAlert alert)
    {
        var tags = Encode(string.Join(", ", alert.RequiredTags));
        return $"""
            <div style="font-family: -apple-system, Segoe UI, Roboto, sans-serif; color: #111;">
              <h2 style="margin: 0 0 12px;">Integration can't be routed</h2>
              <p style="margin: 0 0 16px;">
                <strong>{Encode(alert.IntegrationName)}</strong> can't run in <strong>{Encode(alert.Environment)}</strong>
                because no live agent there offers the capabilities it requires. Its work will queue until a
                matching agent connects.
              </p>
              <table cellpadding="4" style="border-collapse: collapse;">
                <tr><td><strong>Required capabilities</strong></td><td>{tags}</td></tr>
                <tr><td><strong>Detected at</strong></td><td>{alert.DetectedAt:u}</td></tr>
              </table>
              <p style="margin: 16px 0 0;">Start or fix an agent in this environment whose configured tags include all of the above.</p>
            </div>
            """;
    }

    public static string JsonPayload(UnroutableIntegrationAlert alert) =>
        JsonSerializer.Serialize(new
        {
            type = "integration.unroutable",
            tenantId = alert.TenantId,
            integrationId = alert.IntegrationId,
            integrationName = alert.IntegrationName,
            slug = alert.Slug,
            environment = alert.Environment,
            requiredTags = alert.RequiredTags,
            detectedAt = alert.DetectedAt
        });

    private static string Encode(string? value) => HttpUtility.HtmlEncode(value ?? "");
}
