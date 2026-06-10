using System.Net;
using System.Net.Http.Json;

namespace Serto.Testing;

/// <summary>
/// Helpers for giving an integration a fake <see cref="HttpClient"/> in tests, so HTTP-calling
/// integrations can be unit-tested without reaching real endpoints. Assign the result to
/// <see cref="TestIntegrationContext.Http"/> (the HTTP connector uses the context's client).
/// </summary>
public static class TestHttp
{
    /// <summary>An <see cref="HttpClient"/> that returns the supplied response for every request.</summary>
    public static HttpClient Responding(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new StubHandler(responder));

    /// <summary>An <see cref="HttpClient"/> that returns <paramref name="body"/> as JSON for every request.</summary>
    public static HttpClient RespondingJson(object body, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        Responding(_ => new HttpResponseMessage(statusCode) { Content = JsonContent.Create(body) });

    /// <summary>
    /// An <see cref="HttpClient"/> backed by a <see cref="RecordingHttpHandler"/> that captures every
    /// request, so a test can assert on the URL, headers, or call count after running the integration.
    /// </summary>
    public static HttpClient Recording(out RecordingHttpHandler handler, Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        handler = new RecordingHttpHandler(responder ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)));
        return new HttpClient(handler);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(responder(request));
    }
}

/// <summary>An <see cref="HttpMessageHandler"/> that records every request and returns a configured response.</summary>
public sealed class RecordingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(responder(request));
    }
}
