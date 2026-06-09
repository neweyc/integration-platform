using ControlPlane.Infrastructure;
using ControlPlane.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Features.Alerts;

public static class AlertEndpoints
{
    public static IEndpointRouteBuilder MapAlertEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/alerts").WithTags("Alerts").RequireAuthorization();

        // Tenant-default alert configuration.
        group.MapGet("/settings", async (
            IDispatcher dispatcher, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new GetTenantAlertSettingsCommand(currentUser.TenantId), ct);
            return Results.Ok(result);
        }).RequirePermission(Permission.ViewAlerts);

        group.MapPut("/settings", async (
            [FromBody] UpdateTenantAlertSettingsRequest request,
            IDispatcher dispatcher, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new UpdateTenantAlertSettingsCommand(
                    currentUser.TenantId,
                    request.EmailEnabled,
                    request.EmailRecipients,
                    request.SmtpHost,
                    request.SmtpPort ?? 587,
                    request.SmtpUseStartTls ?? true,
                    request.SmtpUsername,
                    request.SmtpPassword,
                    request.SmtpFromAddress,
                    request.SmtpFromName,
                    request.WebhookEnabled,
                    request.WebhookUrl,
                    request.WebhookSecret),
                ct);
            return Results.Ok(result);
        }).RequirePermission(Permission.ManageAlerts);

        // Send a test alert through the current tenant-default configuration.
        group.MapPost("/settings/test", async (
            IDispatcher dispatcher, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new SendTestAlertCommand(currentUser.TenantId, null), ct);
            return Results.Ok(result);
        }).RequirePermission(Permission.ManageAlerts);

        // Per-integration override.
        group.MapGet("/integrations/{integrationId:guid}/settings", async (
            Guid integrationId,
            IDispatcher dispatcher, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new GetIntegrationAlertSettingsCommand(currentUser.TenantId, integrationId), ct);
            return Results.Ok(result);
        }).RequirePermission(Permission.ViewAlerts);

        group.MapPut("/integrations/{integrationId:guid}/settings", async (
            Guid integrationId,
            [FromBody] UpdateIntegrationAlertSettingsRequest request,
            IDispatcher dispatcher, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new UpdateIntegrationAlertSettingsCommand(
                    currentUser.TenantId,
                    integrationId,
                    request.Mode,
                    request.EmailEnabled,
                    request.EmailRecipients,
                    request.WebhookEnabled,
                    request.WebhookUrl,
                    request.WebhookSecret),
                ct);
            return Results.Ok(result);
        }).RequirePermission(Permission.ManageAlerts);

        // Send a test alert through a specific integration's effective configuration.
        group.MapPost("/integrations/{integrationId:guid}/settings/test", async (
            Guid integrationId,
            IDispatcher dispatcher, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new SendTestAlertCommand(currentUser.TenantId, integrationId), ct);
            return Results.Ok(result);
        }).RequirePermission(Permission.ManageAlerts);

        return app;
    }
}
