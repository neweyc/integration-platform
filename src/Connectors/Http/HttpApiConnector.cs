using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Serto.Sdk;

namespace Serto.Connectors.Http;

public sealed class HttpApiConnector
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IIntegrationContext _context;
    private readonly string? _baseUrl;
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _query = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<HttpStatusCode> _retryStatusCodes = [HttpStatusCode.TooManyRequests];
    private HttpAuthConfiguration? _auth;
    private string? _idempotencyKey;
    private string _idempotencyHeaderName = "Idempotency-Key";
    private int _maxRetries = 2;
    private TimeSpan _retryBackoff = TimeSpan.FromSeconds(1);
    private TimeSpan _maxRetryDelay = TimeSpan.FromSeconds(30);

    public HttpApiConnector(IIntegrationContext context, string? baseUrl = null)
    {
        // A base URL is optional (callers may use absolute paths), but if one is given it must be an
        // absolute http/https URL — otherwise requests fail later with an opaque URI error.
        if (baseUrl is not null
            && (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new ArgumentException(
                $"HttpConnector: '{baseUrl}' is not a valid absolute http(s) base URL.", nameof(baseUrl));
        }

        _context = context;
        _baseUrl = baseUrl;
    }

    public HttpApiConnector WithBearerToken(string secretKey)
    {
        _auth = new BearerSecretAuth(secretKey);
        return this;
    }

    public HttpApiConnector WithApiKeyHeader(string headerName, string secretKey)
    {
        _auth = new ApiKeyHeaderSecretAuth(headerName, secretKey);
        return this;
    }

    public HttpApiConnector WithApiKeyQuery(string parameterName, string secretKey)
    {
        _auth = new ApiKeyQuerySecretAuth(parameterName, secretKey);
        return this;
    }

    public HttpApiConnector WithBasicAuth(string usernameSecretKey, string passwordSecretKey)
    {
        _auth = new BasicSecretAuth(usernameSecretKey, passwordSecretKey);
        return this;
    }

    public HttpApiConnector WithHeader(string name, string value)
    {
        _headers[name] = value;
        return this;
    }

    public HttpApiConnector WithQueryParameter(string name, string value)
    {
        _query[name] = value;
        return this;
    }

    public HttpApiConnector WithIdempotencyKey(string key, string headerName = "Idempotency-Key")
    {
        _idempotencyKey = key;
        _idempotencyHeaderName = headerName;
        return this;
    }

    public HttpApiConnector WithRetryPolicy(
        int maxRetries,
        TimeSpan? backoff = null,
        TimeSpan? maxRetryDelay = null,
        IReadOnlyCollection<HttpStatusCode>? retryStatusCodes = null)
    {
        if (maxRetries < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRetries), "Max retries cannot be negative.");

        _maxRetries = maxRetries;
        _retryBackoff = backoff ?? TimeSpan.FromSeconds(1);
        _maxRetryDelay = maxRetryDelay ?? TimeSpan.FromSeconds(30);

        _retryStatusCodes.Clear();
        foreach (var statusCode in retryStatusCodes ?? [HttpStatusCode.TooManyRequests])
            _retryStatusCodes.Add(statusCode);

        return this;
    }

    public async Task<T?> GetJsonAsync<T>(string path, CancellationToken ct = default) =>
        (await SendJsonAsync<T>(HttpMethod.Get, path, body: null, ct)).Data;

    public async Task<TResponse?> PostJsonAsync<TRequest, TResponse>(
        string path,
        TRequest payload,
        CancellationToken ct = default) =>
        (await SendJsonAsync<TResponse>(HttpMethod.Post, path, payload, ct)).Data;

    public async Task PostJsonAsync<TRequest>(string path, TRequest payload, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, path, payload, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<TResponse?> PutJsonAsync<TRequest, TResponse>(
        string path,
        TRequest payload,
        CancellationToken ct = default) =>
        (await SendJsonAsync<TResponse>(HttpMethod.Put, path, payload, ct)).Data;

    public async Task<TResponse?> PatchJsonAsync<TRequest, TResponse>(
        string path,
        TRequest payload,
        CancellationToken ct = default) =>
        (await SendJsonAsync<TResponse>(HttpMethod.Patch, path, payload, ct)).Data;

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, path, body: null, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<HttpApiResponse<T?>> SendJsonAsync<T>(
        HttpMethod method,
        string path,
        object? body = null,
        CancellationToken ct = default)
    {
        using var response = await SendAsync(method, path, body, ct);
        await EnsureSuccessAsync(response, ct);

        var value = response.Content.Headers.ContentLength == 0
            ? default
            : await response.Content.ReadFromJsonAsync<T>(DefaultJsonOptions, ct);

        return new HttpApiResponse<T?>(
            value,
            response.StatusCode,
            response.Headers.ToDictionary(
                h => h.Key,
                h => (IReadOnlyList<string>)h.Value.ToList(),
                StringComparer.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<TItem>> GetAllPagesAsync<TPage, TItem>(
        string path,
        Func<TPage, IEnumerable<TItem>> items,
        Func<TPage, string?> nextPath,
        CancellationToken ct = default)
    {
        var all = new List<TItem>();
        var current = path;

        while (!string.IsNullOrWhiteSpace(current))
        {
            var page = await GetJsonAsync<TPage>(current, ct);
            if (page is null)
                break;

            all.AddRange(items(page));
            current = nextPath(page);
        }

        return all;
    }

    public async Task<IReadOnlyList<TItem>> GetOffsetPagesAsync<TPage, TItem>(
        string path,
        Func<TPage, IEnumerable<TItem>> items,
        Func<TPage, bool> hasMore,
        int limit = 100,
        string offsetParameter = "offset",
        string limitParameter = "limit",
        CancellationToken ct = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");

        var all = new List<TItem>();
        var offset = 0;

        while (true)
        {
            var pagePath = AddQuery(path, new Dictionary<string, string>
            {
                [offsetParameter] = offset.ToString(),
                [limitParameter] = limit.ToString()
            });
            var page = await GetJsonAsync<TPage>(pagePath, ct);
            if (page is null)
                break;

            var pageItems = items(page).ToList();
            all.AddRange(pageItems);

            if (!hasMore(page))
                break;

            offset += limit;
        }

        return all;
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        CancellationToken ct = default)
    {
        var bodyBytes = body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body, DefaultJsonOptions);
        HttpResponseMessage? response = null;

        for (var attempt = 0; attempt <= _maxRetries; attempt++)
        {
            response?.Dispose();
            using var request = CreateRequest(method, path, bodyBytes);
            _context.Logger.LogInformation("HTTP {Method} {Uri} attempt {Attempt}", method, SanitizeUriForLog(request.RequestUri), attempt + 1);

            response = await _context.Http.SendAsync(request, ct);
            if (!ShouldRetry(method, response, attempt))
                return response;

            var delay = ResolveRetryDelay(response, attempt);
            _context.Logger.LogWarning(
                "HTTP {Method} {Uri} returned {StatusCode}; retrying in {DelayMs}ms",
                method,
                SanitizeUriForLog(request.RequestUri),
                (int)response.StatusCode,
                delay.TotalMilliseconds);
            await Task.Delay(delay, ct);
        }

        return response!;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, byte[]? bodyBytes)
    {
        var uri = ResolveUri(path);
        var request = new HttpRequestMessage(method, uri);

        foreach (var header in _headers)
            request.Headers.Add(header.Key, header.Value);

        if (!string.IsNullOrWhiteSpace(_idempotencyKey))
        {
            request.Headers.Remove(_idempotencyHeaderName);
            request.Headers.Add(_idempotencyHeaderName, _idempotencyKey);
        }

        ApplyAuth(request, ref uri);
        request.RequestUri = uri;

        if (bodyBytes is not null)
            request.Content = new ByteArrayContent(bodyBytes)
            {
                Headers = { ContentType = MediaTypeHeaderValue.Parse("application/json") }
            };

        return request;
    }

    private Uri ResolveUri(string path)
    {
        var uri = _baseUrl is not null
            ? new Uri(new Uri(_baseUrl.EndsWith('/') ? _baseUrl : _baseUrl + "/"), path.StartsWith('/') ? path[1..] : path)
            : new Uri(path, UriKind.RelativeOrAbsolute);

        return _query.Count == 0 ? uri : AddQuery(uri, _query);
    }

    private void ApplyAuth(HttpRequestMessage request, ref Uri uri)
    {
        switch (_auth)
        {
            case null:
                return;
            case BearerSecretAuth bearer:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetSecret(bearer.SecretKey));
                return;
            case ApiKeyHeaderSecretAuth header:
                request.Headers.Remove(header.HeaderName);
                request.Headers.Add(header.HeaderName, GetSecret(header.SecretKey));
                return;
            case ApiKeyQuerySecretAuth query:
                uri = AddQuery(uri, new Dictionary<string, string> { [query.ParameterName] = GetSecret(query.SecretKey) });
                return;
            case BasicSecretAuth basic:
                var raw = $"{GetSecret(basic.UsernameSecretKey)}:{GetSecret(basic.PasswordSecretKey)}";
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
                return;
        }
    }

    private string GetSecret(string key) =>
        _context.Secrets.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"Secret '{key}' not found for HTTP connector.");

    private bool ShouldRetry(HttpMethod method, HttpResponseMessage response, int attempt) =>
        attempt < _maxRetries
        && CanRetryMethod(method)
        && (_retryStatusCodes.Contains(response.StatusCode)
            || ((int)response.StatusCode >= 500 && (int)response.StatusCode <= 599));

    // Idempotent verbs are always safe to retry. POST/PATCH are only retried when an idempotency key
    // is set, so a retried write cannot silently duplicate a side effect the server may already have
    // applied for a request that failed after it was processed.
    private bool CanRetryMethod(HttpMethod method) =>
        method == HttpMethod.Get
        || method == HttpMethod.Put
        || method == HttpMethod.Delete
        || method == HttpMethod.Head
        || method == HttpMethod.Options
        || !string.IsNullOrWhiteSpace(_idempotencyKey);

    // The query-parameter auth path embeds a secret in the URL. Header/bearer/basic auth keep the
    // secret in request headers (never logged), but a query API key would otherwise be written to the
    // execution log via the request URI — redact just that parameter before logging.
    private string SanitizeUriForLog(Uri? uri)
    {
        if (uri is null)
            return "(none)";

        if (_auth is not ApiKeyQuerySecretAuth queryAuth)
            return uri.ToString();

        var raw = uri.ToString();
        var queryStart = raw.IndexOf('?');
        if (queryStart < 0)
            return raw;

        var encodedName = WebUtility.UrlEncode(queryAuth.ParameterName);
        var pairs = raw[(queryStart + 1)..].Split('&');
        for (var i = 0; i < pairs.Length; i++)
        {
            var eq = pairs[i].IndexOf('=');
            var key = eq < 0 ? pairs[i] : pairs[i][..eq];
            if (string.Equals(key, queryAuth.ParameterName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, encodedName, StringComparison.OrdinalIgnoreCase))
                pairs[i] = key + "=***";
        }

        return raw[..queryStart] + "?" + string.Join("&", pairs);
    }

    private TimeSpan ResolveRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return ClampDelay(delta);

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return ClampDelay(delay <= TimeSpan.Zero ? TimeSpan.Zero : delay);
        }

        var exponential = TimeSpan.FromMilliseconds(_retryBackoff.TotalMilliseconds * Math.Pow(2, attempt));
        return ClampDelay(exponential);
    }

    private TimeSpan ClampDelay(TimeSpan delay) =>
        delay > _maxRetryDelay ? _maxRetryDelay : delay;

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var content = await response.Content.ReadAsStringAsync(ct);
        var snippet = content.Length > 1000 ? content[..1000] : content;
        _context.Logger.LogError(
            "HTTP request failed with status {StatusCode}: {Content}",
            (int)response.StatusCode,
            snippet);

        throw new HttpApiConnectorException(
            response.StatusCode,
            response.RequestMessage?.Method.Method ?? "HTTP",
            response.RequestMessage?.RequestUri,
            snippet);
    }

    private static Uri AddQuery(Uri uri, IReadOnlyDictionary<string, string> query)
    {
        if (!uri.IsAbsoluteUri)
            return new Uri(AddQuery(uri.ToString(), query), UriKind.Relative);

        var builder = new UriBuilder(uri);
        builder.Query = BuildQuery(builder.Query, query);
        return builder.Uri;
    }

    private static string AddQuery(string path, IReadOnlyDictionary<string, string> query)
    {
        var separator = path.Contains('?') ? "&" : "?";
        return path + separator + BuildQuery(string.Empty, query);
    }

    private static string BuildQuery(string existingQuery, IReadOnlyDictionary<string, string> query)
    {
        var parts = new List<string>();
        var normalizedExisting = existingQuery.TrimStart('?');
        if (!string.IsNullOrWhiteSpace(normalizedExisting))
            parts.Add(normalizedExisting);

        parts.AddRange(query.Select(kvp =>
            $"{WebUtility.UrlEncode(kvp.Key)}={WebUtility.UrlEncode(kvp.Value)}"));

        return string.Join("&", parts);
    }
}

public sealed class HttpApiConnectorException : Exception
{
    public HttpApiConnectorException(HttpStatusCode statusCode, string method, Uri? uri, string responseSnippet)
        : base($"HTTP {method} {uri} failed with {(int)statusCode} {statusCode}.")
    {
        StatusCode = statusCode;
        Method = method;
        Uri = uri;
        ResponseSnippet = responseSnippet;
    }

    public HttpStatusCode StatusCode { get; }
    public string Method { get; }
    public Uri? Uri { get; }
    public string ResponseSnippet { get; }
}

public record HttpApiResponse<T>(
    T Data,
    HttpStatusCode StatusCode,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers);

internal abstract record HttpAuthConfiguration;
internal sealed record BearerSecretAuth(string SecretKey) : HttpAuthConfiguration;
internal sealed record ApiKeyHeaderSecretAuth(string HeaderName, string SecretKey) : HttpAuthConfiguration;
internal sealed record ApiKeyQuerySecretAuth(string ParameterName, string SecretKey) : HttpAuthConfiguration;
internal sealed record BasicSecretAuth(string UsernameSecretKey, string PasswordSecretKey) : HttpAuthConfiguration;

public static class IntegrationContextExtensions
{
    public static HttpApiConnector HttpConnector(this IIntegrationContext context, string? baseUrl = null)
    {
        return new HttpApiConnector(context, baseUrl);
    }
}
