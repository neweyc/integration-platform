using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ControlPlane.Infrastructure.RateLimiting;

// Options bound from the "RateLimit" configuration section. All limits are per client IP and
// per fixed time window. Two tiers exist: a generous global limit across the whole API, and a
// much stricter limit for sensitive unauthenticated endpoints (login, setup, password reset,
// token refresh) where brute-force and abuse are the real risk.
public sealed class RateLimitOptions
{
    // The named policy applied to sensitive auth endpoints via .RequireRateLimiting(AuthPolicy).
    public const string AuthPolicy = "auth";

    // When false, the rate-limiting middleware is not added to the pipeline at all. Tests set this
    // off so repeated logins/requests in a single test don't trip the limiter.
    public bool Enabled { get; set; } = true;

    // Global limit: requests per window per IP across every rate-limited endpoint.
    public int PermitLimit { get; set; } = 300;
    public int WindowSeconds { get; set; } = 60;

    // Stricter limit for the "auth" policy: requests per window per IP.
    public int AuthPermitLimit { get; set; } = 10;
    public int AuthWindowSeconds { get; set; } = 60;
}

public static class RateLimitingExtensions
{
    public static IServiceCollection AddSertoRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind via the options pattern rather than eagerly. Under the test host, configuration
        // sources are layered after service registration runs, so an eager .Get<>() here would
        // miss them. Reading IOptions after the host is built (and per request) sees final config.
        services.Configure<RateLimitOptions>(configuration.GetSection("RateLimit"));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Global limiter — a fixed window per client IP across all endpoints that reach the
            // middleware. Static assets are served earlier in the pipeline, so a browser loading
            // the SPA's bundle does not burn through this budget.
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var options = CurrentOptions(context);
                return RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitLimit,
                    Window = TimeSpan.FromSeconds(options.WindowSeconds),
                    QueueLimit = 0
                });
            });

            // Sensitive auth endpoints get a second, stricter bucket on top of the global one.
            limiter.AddPolicy(RateLimitOptions.AuthPolicy, context =>
            {
                var options = CurrentOptions(context);
                return RateLimitPartition.GetFixedWindowLimiter("auth:" + ClientKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.AuthPermitLimit,
                    Window = TimeSpan.FromSeconds(options.AuthWindowSeconds),
                    QueueLimit = 0
                });
            });

            limiter.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                // Tell well-behaved clients when to come back.
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString();

                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    title = "Too Many Requests",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = "Rate limit exceeded. Please slow down and retry shortly."
                }, ct);
            };
        });

        return services;
    }

    private static RateLimitOptions CurrentOptions(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

    // Partition by client IP. Behind a reverse proxy this is the proxy address unless forwarded
    // headers are configured; that is acceptable for the self-hosted v1 and documented as such.
    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
