using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
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

    private sealed record ResponseDto(bool Ok);
    private sealed record PageDto(IReadOnlyList<int> Items, string? Next);
}
