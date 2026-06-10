using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Features.Billing;

public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/billing")
            .WithTags("Billing")
            .RequireAuthorization();

        // Current plan, subscription status, and usage for the tenant.
        group.MapGet("/current", async (
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new GetBillingStatusCommand(currentUser.TenantId), ct);
            return Results.Ok(result);
        }).RequirePermission(Permission.ManageBilling);

        // Start a checkout for a self-serve plan; returns the Stripe-hosted URL to redirect to.
        group.MapPost("/checkout", async (
            [FromBody] CheckoutRequestBody request,
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new CreateCheckoutSessionCommand(currentUser.TenantId, request.Plan), ct);
            return Results.Ok(result);
        }).RequirePermission(Permission.ManageBilling);

        // Open the Stripe Billing Portal to manage an existing subscription.
        group.MapPost("/portal", async (
            IDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new CreatePortalSessionCommand(currentUser.TenantId), ct);
            return Results.Ok(result);
        }).RequirePermission(Permission.ManageBilling);

        // Stripe subscription webhook. Public and signature-verified — Stripe calls this server-to-server.
        app.MapPost("/api/billing/webhook", async (
            HttpRequest httpRequest,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            using var reader = new StreamReader(httpRequest.Body);
            var payload = await reader.ReadToEndAsync(ct);
            var signature = httpRequest.Headers["Stripe-Signature"].ToString();

            await dispatcher.SendAsync(new HandleStripeWebhookCommand(payload, signature), ct);
            return Results.Ok();
        }).WithTags("Billing");

        return app;
    }
}

public record CheckoutRequestBody(string Plan);
