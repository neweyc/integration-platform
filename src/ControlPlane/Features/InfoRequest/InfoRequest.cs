using System.Text.Encodings.Web;
using ControlPlane.Features.Alerts.Email;
using ControlPlane.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ControlPlane.Features.InfoRequest;

public class InfoRequestOptions
{
    public const string SectionName = "InfoRequest";

    // Where "request more info" submissions are emailed. Override via InfoRequest:Recipient.
    public string Recipient { get; set; } = "info@craytech-solutions.com";
}

// The public form payload.
public record InfoRequestForm(string? Name, string? Email, string? Company, string? Message);

public enum InfoRequestStatus { Sent, Invalid, NotConfigured, SendFailed }

public record InfoRequestOutcome(InfoRequestStatus Status, string? Field = null, string? Message = null);

// Validates a "request more info" submission and emails it via the platform ZeptoMail sender. Sends only
// an email — it never touches the database, so it stays available even in maintenance mode.
public sealed class InfoRequestHandler(
    IEnumerable<IEmailSender> senders,
    ZeptoOptions zepto,
    IOptions<InfoRequestOptions> options,
    ILogger<InfoRequestHandler> logger)
{
    public async Task<InfoRequestOutcome> SubmitAsync(InfoRequestForm form, CancellationToken ct)
    {
        var name = form.Name?.Trim() ?? "";
        var email = form.Email?.Trim() ?? "";
        var company = form.Company?.Trim() ?? "";
        var message = form.Message?.Trim() ?? "";

        if (name.Length is 0 or > 200)
            return new(InfoRequestStatus.Invalid, "name", "A name is required.");
        if (!IsPlausibleEmail(email) || email.Length > 320)
            return new(InfoRequestStatus.Invalid, "email", "A valid email address is required.");
        if (message.Length is 0 or > 5000)
            return new(InfoRequestStatus.Invalid, "message", "A message is required.");
        if (company.Length > 200)
            return new(InfoRequestStatus.Invalid, "company", "Company name is too long.");

        if (!zepto.IsConfigured)
        {
            logger.LogWarning("Info request received but email is not configured (Zepto:Token / Zepto:FromAddress).");
            return new(InfoRequestStatus.NotConfigured);
        }

        var zeptoSender = senders.First(s => s.Provider == EmailProvider.Zepto);

        var encodedCompany = company.Length > 0 ? HtmlEncoder.Default.Encode(company) : "";
        var html =
            $"<p><strong>Name:</strong> {HtmlEncoder.Default.Encode(name)}</p>" +
            $"<p><strong>Email:</strong> {HtmlEncoder.Default.Encode(email)}</p>" +
            (company.Length > 0 ? $"<p><strong>Company:</strong> {encodedCompany}</p>" : "") +
            $"<p><strong>Message:</strong></p><p>{HtmlEncoder.Default.Encode(message).Replace("\n", "<br>")}</p>";
        var text =
            $"Name: {name}\nEmail: {email}\n" +
            (company.Length > 0 ? $"Company: {company}\n" : "") +
            $"\n{message}\n";
        var subject = $"Serto info request from {name}" + (company.Length > 0 ? $" ({company})" : "");

        try
        {
            await zeptoSender.SendAsync(new EmailMessage(
                FromAddress: zepto.FromAddress!,
                FromName: zepto.FromName ?? "Serto",
                Recipients: [options.Value.Recipient],
                Subject: subject,
                HtmlBody: html,
                TextBody: text,
                Smtp: null), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send info request email.");
            return new(InfoRequestStatus.SendFailed);
        }

        return new(InfoRequestStatus.Sent);
    }

    // Deliberately permissive: a real address has one '@' that is neither first nor last and no spaces.
    // Heavy RFC validation rejects valid addresses; the email only has to be reachable for a human reply.
    private static bool IsPlausibleEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 && at < email.Length - 1 && email.IndexOf('@', at + 1) < 0 && !email.Contains(' ');
    }
}

public static class InfoRequestEndpoints
{
    public static IEndpointRouteBuilder MapInfoRequestEndpoints(this IEndpointRouteBuilder app)
    {
        // Public lead-capture form. Rate-limited like the auth endpoints; exempt from the maintenance
        // breaker (see MaintenanceOptions.AllowedPaths) because it sends an email and never writes to the DB.
        app.MapPost("/api/info-request", async (
            [FromBody] InfoRequestForm form,
            InfoRequestHandler handler,
            CancellationToken ct) =>
        {
            var outcome = await handler.SubmitAsync(form, ct);
            return outcome.Status switch
            {
                InfoRequestStatus.Sent => Results.Ok(new { sent = true }),
                InfoRequestStatus.Invalid =>
                    Results.ValidationProblem(new Dictionary<string, string[]> { [outcome.Field!] = [outcome.Message!] }),
                InfoRequestStatus.NotConfigured =>
                    Results.Problem("Info requests can't be sent right now. Please email us directly.",
                        statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Problem("We couldn't send your request right now. Please try again later.",
                        statusCode: StatusCodes.Status502BadGateway),
            };
        }).RequireRateLimiting(RateLimitOptions.AuthPolicy);

        return app;
    }
}
