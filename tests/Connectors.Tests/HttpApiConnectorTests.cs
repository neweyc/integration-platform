using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Serto.Connectors.Http;
using Serto.Testing;

namespace Connectors.Tests;

public class HttpApiConnectorTests
{
    [Fact]
    public async Task GetJsonAsync_AppliesBearerTokenAndHeaders()
    {
        using var handler = new RecordingHandler(_ => JsonResponse(new { ok = true }));
        var context = Context(handler, new Dictionary<string, string> { ["API_TOKEN"] = "secret-token" });

        var result = await context.HttpConnector("https://api.example.com")
            .WithBearerToken("API_TOKEN")
            .WithHeader("X-Tenant", "acme")
            .GetJsonAsync<ResponseDto>("/orders");

        Assert.True(result!.Ok);
        Assert.Equal("Bearer", handler.Requests.Single().Headers.Authorization!.Scheme);
        Assert.Equal("secret-token", handler.Requests.Single().Headers.Authorization!.Parameter);
        Assert.Equal("acme", handler.Requests.Single().Headers.GetValues("X-Tenant").Single());
    }

    [Fact]
    public async Task GetJsonAsync_AppliesApiKeyQuery()
    {
        using var handler = new RecordingHandler(_ => JsonResponse(new { ok = true }));
        var context = Context(handler, new Dictionary<string, string> { ["API_KEY"] = "key value" });

        await context.HttpConnector("https://api.example.com")
            .WithApiKeyQuery("api_key", "API_KEY")
            .WithQueryParameter("region", "us")
            .GetJsonAsync<ResponseDto>("/orders?status=open");

        var uri = handler.Requests.Single().RequestUri!.ToString();
        Assert.Contains("status=open", uri);
        Assert.Contains("region=us", uri);
        Assert.Contains("api_key=key+value", uri);
    }

    [Fact]
    public async Task PostJsonAsync_AppliesBasicAuthAndIdempotencyHeader()
    {
        using var handler = new RecordingHandler(_ => JsonResponse(new { ok = true }));
        var context = Context(handler, new Dictionary<string, string>
        {
            ["USER"] = "alice",
            ["PASS"] = "password"
        });

        await context.HttpConnector("https://api.example.com")
            .WithBasicAuth("USER", "PASS")
            .WithIdempotencyKey("order-123")
            .PostJsonAsync<object, ResponseDto>("/orders", new { id = 123 });

        var request = handler.Requests.Single();
        Assert.Equal("order-123", request.Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String("alice:password"u8.ToArray()), request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task GetJsonAsync_RetriesRateLimitedRequests()
    {
        var attempts = 0;
        using var handler = new RecordingHandler(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                var rateLimited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                rateLimited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return rateLimited;
            }

            return JsonResponse(new { ok = true });
        });

        var context = Context(handler);

        var result = await context.HttpConnector("https://api.example.com")
            .WithRetryPolicy(1, TimeSpan.Zero, TimeSpan.Zero)
            .GetJsonAsync<ResponseDto>("/orders");

        Assert.True(result!.Ok);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task PostJsonAsync_DoesNotRetryWithoutIdempotencyKey()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var context = Context(handler);

        await Assert.ThrowsAsync<HttpApiConnectorException>(() =>
            context.HttpConnector("https://api.example.com")
                .WithRetryPolicy(2, TimeSpan.Zero, TimeSpan.Zero)
                .PostJsonAsync<object, ResponseDto>("/orders", new { id = 1 }));

        // A non-idempotent POST must not be retried unless an idempotency key makes it safe.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PostJsonAsync_RetriesServerErrorWhenIdempotencyKeySet()
    {
        var attempts = 0;
        using var handler = new RecordingHandler(_ =>
        {
            attempts++;
            return attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : JsonResponse(new { ok = true });
        });
        var context = Context(handler);

        var result = await context.HttpConnector("https://api.example.com")
            .WithIdempotencyKey("order-1")
            .WithRetryPolicy(2, TimeSpan.Zero, TimeSpan.Zero)
            .PostJsonAsync<object, ResponseDto>("/orders", new { id = 1 });

        Assert.True(result!.Ok);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ApiKeyQuery_SecretIsRedactedInLogs()
    {
        using var handler = new RecordingHandler(_ => JsonResponse(new { ok = true }));
        var logger = new CapturingLogger();
        var context = Context(handler, new Dictionary<string, string> { ["API_KEY"] = "super-secret-value" });
        context.Logger = logger;

        await context.HttpConnector("https://api.example.com")
            .WithApiKeyQuery("api_key", "API_KEY")
            .GetJsonAsync<ResponseDto>("/orders");

        // The key reaches the wire...
        Assert.Contains("api_key=super-secret-value", handler.Requests.Single().RequestUri!.ToString());
        // ...but never the captured execution logs.
        var logged = string.Join("\n", logger.Messages);
        Assert.Contains("api_key=***", logged);
        Assert.DoesNotContain("super-secret-value", logged);
    }

    [Fact]
    public async Task GetJsonAsync_ThrowsNormalizedExceptionOnFailure()
    {
        using var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":"bad"}""")
            });
        var context = Context(handler);

        var ex = await Assert.ThrowsAsync<HttpApiConnectorException>(() =>
            context.HttpConnector("https://api.example.com").GetJsonAsync<ResponseDto>("/orders"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("bad", ex.ResponseSnippet);
        Assert.Equal("GET", ex.Method);
    }

    [Fact]
    public async Task GetAllPagesAsync_FollowsNextPath()
    {
        using var handler = new RecordingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/page-1" => JsonResponse(new PageDto([1, 2], "/page-2")),
                "/page-2" => JsonResponse(new PageDto([3], null)),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        var context = Context(handler);

        var items = await context.HttpConnector("https://api.example.com")
            .GetAllPagesAsync<PageDto, int>(
                "/page-1",
                page => page.Items,
                page => page.Next);

        Assert.Equal([1, 2, 3], items);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("/relative/path")]
    public void Constructor_InvalidBaseUrl_Throws(string baseUrl)
    {
        var context = new TestIntegrationContext();

        Assert.Throws<ArgumentException>(() => new HttpApiConnector(context, baseUrl));
    }

    [Fact]
    public void Constructor_NullBaseUrl_IsAllowed()
    {
        var context = new TestIntegrationContext();

        var exception = Record.Exception(() => new HttpApiConnector(context, baseUrl: null));

        Assert.Null(exception);
    }

    [Fact]
    public async Task MissingSecret_ThrowsClearError()
    {
        using var handler = new RecordingHandler(_ => JsonResponse(new { ok = true }));
        var context = Context(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.HttpConnector("https://api.example.com")
                .WithBearerToken("MISSING")
                .GetJsonAsync<ResponseDto>("/orders"));

        Assert.Contains("MISSING", ex.Message);
    }

    private static TestIntegrationContext Context(
        RecordingHandler handler,
        IReadOnlyDictionary<string, string>? secrets = null) =>
        new()
        {
            Http = new HttpClient(handler),
            Secrets = secrets ?? new Dictionary<string, string>()
        };

    private static HttpResponseMessage JsonResponse<T>(T value) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
        };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            var response = responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
                clone.Headers.Add(header.Key, header.Value);
            return clone;
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed record ResponseDto(bool Ok);
    private sealed record PageDto(IReadOnlyList<int> Items, string? Next);
}
