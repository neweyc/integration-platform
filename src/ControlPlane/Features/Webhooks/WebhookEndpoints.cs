using ControlPlane.Infrastructure;
using Microsoft.AspNetCore.Http.Features;

namespace ControlPlane.Features.Webhooks;

public static class WebhookHeaders
{
    public const string Signature = "X-Integration-Signature";
    public const string Delivery = "X-Integration-Delivery";
    public const string Timestamp = "X-Integration-Timestamp";
    public const string Algorithm = "HMAC-SHA256";

    // Deliveries whose timestamp is further than this from now are rejected as replays/stale.
    public const int ToleranceSeconds = 300;

    public const string SignatureFormat =
        "sha256=<lowercase hex HMAC-SHA256 of \"{X-Integration-Timestamp}.{raw request body}\">";
}

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/webhooks").WithTags("Webhooks");

        group.MapPost("/{tenantSlug}/{integrationSlug}/{triggerSlug}", async (
            string tenantSlug,
            string integrationSlug,
            string triggerSlug,
            HttpContext http,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            // Bound the request body so a malicious sender can't OOM the server.
            // Kestrel aborts the read with BadHttpRequestException (413) past this limit.
            var sizeFeature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false })
                sizeFeature.MaxRequestBodySize = DeliverWebhookHandler.MaxPayloadBytes;

            // Fast reject when the sender declares an oversized Content-Length.
            if (http.Request.ContentLength is > DeliverWebhookHandler.MaxPayloadBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            using var buffer = new MemoryStream();
            await http.Request.Body.CopyToAsync(buffer, ct);

            var result = await dispatcher.SendAsync(
                new DeliverWebhookCommand(
                    tenantSlug,
                    integrationSlug,
                    triggerSlug,
                    http.Request.Headers[WebhookHeaders.Signature].FirstOrDefault(),
                    http.Request.Headers[WebhookHeaders.Timestamp].FirstOrDefault(),
                    http.Request.Headers[WebhookHeaders.Delivery].FirstOrDefault(),
                    buffer.ToArray()),
                ct);

            return result.Queued
                ? Results.Accepted($"/api/agent/integrations", result)
                : Results.Ok(result);
        });

        return app;
    }
}
