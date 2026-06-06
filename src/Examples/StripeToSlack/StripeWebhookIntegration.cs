using Serto.Sdk;
using Serto.Connectors.Http;
using Microsoft.Extensions.Logging;

namespace Examples.StripeToSlack;

[WebhookIntegration("Stripe to Slack", "stripe-to-slack", Description = "Notify Slack when a Stripe payment is received.")]
public class StripeWebhookIntegration : IIntegration
{
    public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(context.Payload))
        {
            context.Logger.LogWarning("Empty payload received.");
            return;
        }

        context.Logger.LogInformation("Processing Stripe webhook...");

        // In a real app, you'd parse the Stripe JSON payload
        // var stripeEvent = JsonDocument.Parse(context.Payload);
        
        var slack = context.HttpConnector("https://hooks.slack.com/services/")
                           .WithBearerToken("SLACK_WEBHOOK_SECRET");

        await slack.PostJsonAsync("", new
        {
            text = "*New Stripe Payment Received!*\nCheck the dashboard for details."
        }, ct);

        context.Logger.LogInformation("Slack notification sent.");
    }
}
