using System.Net.Http.Headers;
using System.Net.Http.Json;
using Serto.Sdk;
using Microsoft.Extensions.Logging;

namespace Serto.Connectors.Http;

public sealed class HttpApiConnector
{
    private readonly IIntegrationContext _context;
    private readonly string? _baseUrl;
    private string? _bearerTokenSecretKey;
    private readonly Dictionary<string, string> _headers = new();

    public HttpApiConnector(IIntegrationContext context, string? baseUrl = null)
    {
        _context = context;
        _baseUrl = baseUrl;
    }

    public HttpApiConnector WithBearerToken(string secretKey)
    {
        _bearerTokenSecretKey = secretKey;
        return this;
    }

    public HttpApiConnector WithHeader(string name, string value)
    {
        _headers[name] = value;
        return this;
    }

    public async Task<T?> GetJsonAsync<T>(string path, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        _context.Logger.LogInformation("HTTP GET {Uri}", request.RequestUri);

        try
        {
            using var response = await _context.Http.SendAsync(request, ct);
            await EnsureSuccessAsync(response);
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _context.Logger.LogError(ex, "HTTP GET {Path} failed", path);
            throw;
        }
    }

    public async Task<TResponse?> PostJsonAsync<TRequest, TResponse>(string path, TRequest payload, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = JsonContent.Create(payload);
        _context.Logger.LogInformation("HTTP POST {Uri}", request.RequestUri);

        try
        {
            using var response = await _context.Http.SendAsync(request, ct);
            await EnsureSuccessAsync(response);
            return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _context.Logger.LogError(ex, "HTTP POST {Path} failed", path);
            throw;
        }
    }

    public async Task PostJsonAsync<TRequest>(string path, TRequest payload, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = JsonContent.Create(payload);
        _context.Logger.LogInformation("HTTP POST {Uri}", request.RequestUri);

        try
        {
            using var response = await _context.Http.SendAsync(request, ct);
            await EnsureSuccessAsync(response);
        }
        catch (Exception ex)
        {
            _context.Logger.LogError(ex, "HTTP POST {Path} failed", path);
            throw;
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var uri = _baseUrl != null 
            ? new Uri(new Uri(_baseUrl.EndsWith("/") ? _baseUrl : _baseUrl + "/"), path.StartsWith("/") ? path[1..] : path) 
            : new Uri(path, UriKind.RelativeOrAbsolute);

        var request = new HttpRequestMessage(method, uri);

        foreach (var header in _headers)
        {
            request.Headers.Add(header.Key, header.Value);
        }

        if (_bearerTokenSecretKey != null)
        {
            if (_context.Secrets.TryGetValue(_bearerTokenSecretKey, out var token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                throw new InvalidOperationException($"Secret '{_bearerTokenSecretKey}' not found for bearer token.");
            }
        }

        return request;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            _context.Logger.LogError("HTTP request failed with status {StatusCode}: {Content}", response.StatusCode, content);
            response.EnsureSuccessStatusCode();
        }
    }
}

public static class IntegrationContextExtensions
{
    public static HttpApiConnector HttpConnector(this IIntegrationContext context, string? baseUrl = null)
    {
        return new HttpApiConnector(context, baseUrl);
    }
}
