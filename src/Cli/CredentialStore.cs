using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cli;

/// <summary>
/// Persists the user's API token between commands so they don't have to re-enter it on every deploy.
/// Tokens are stored per control-plane URL in <c>~/.serto/credentials.json</c>, along with the URL of the
/// most recent login as the default. <c>serto deploy</c> uses that default when no <c>--url</c> is given,
/// so logging in once is enough.
///
/// The file is written with owner-only permissions on Unix (the same approach the GitHub, AWS, and npm
/// CLIs use): the token is a revocable, tenant-scoped credential, so the practical protection is keeping it
/// unreadable by other local users rather than encrypting it at rest.
/// </summary>
public sealed class CredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;

    // The override path exists so tests can use a temp file instead of the real home directory.
    public CredentialStore(string? filePath = null)
    {
        _filePath = filePath ?? DefaultPath();
    }

    public string FilePath => _filePath;

    public static string DefaultPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".serto", "credentials.json");
    }

    public string? GetToken(string controlPlaneUrl)
    {
        var file = Load();
        return file.Credentials.TryGetValue(Normalize(controlPlaneUrl), out var entry)
               && !string.IsNullOrWhiteSpace(entry.Token)
            ? entry.Token
            : null;
    }

    /// <summary>The URL of the most recent login, used by deploy when no <c>--url</c> is supplied.</summary>
    public string? GetDefaultUrl() => Load().DefaultUrl;

    public void Save(string controlPlaneUrl, string token)
    {
        var file = Load();
        var normalized = Normalize(controlPlaneUrl);
        file.Credentials[normalized] = new Entry(token.Trim());
        // The control plane just logged into becomes the default deploy target.
        file.DefaultUrl = normalized;
        Write(file);
    }

    public bool Remove(string controlPlaneUrl)
    {
        var file = Load();
        var normalized = Normalize(controlPlaneUrl);
        if (!file.Credentials.Remove(normalized))
            return false;

        // If we removed the default, repoint it at whatever remains (or clear it).
        if (file.DefaultUrl == normalized)
            file.DefaultUrl = file.Credentials.Keys.FirstOrDefault();

        Write(file);
        return true;
    }

    public void Clear()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }

    public IReadOnlyCollection<string> ListUrls() => Load().Credentials.Keys.ToList();

    // URLs are matched case-insensitively and ignoring a trailing slash, so "http://Localhost:5000/"
    // and "http://localhost:5000" resolve to the same saved token.
    internal static string Normalize(string controlPlaneUrl) =>
        controlPlaneUrl.Trim().TrimEnd('/').ToLowerInvariant();

    private CredentialFile Load()
    {
        if (!File.Exists(_filePath))
            return new CredentialFile();

        try
        {
            var json = File.ReadAllText(_filePath);

            // The current format is an object with a "credentials" property; an earlier format was a flat
            // { url: { token } } map. Detect which one this is so an existing login survives the upgrade and
            // the new (possibly empty) format is never misread as a legacy map.
            using var document = JsonDocument.Parse(json);
            var isCurrentFormat = document.RootElement.ValueKind == JsonValueKind.Object
                && (document.RootElement.TryGetProperty("credentials", out _)
                    || document.RootElement.TryGetProperty("defaultUrl", out _));

            if (isCurrentFormat)
                return JsonSerializer.Deserialize<CredentialFile>(json, JsonOptions) ?? new CredentialFile();

            var legacy = JsonSerializer.Deserialize<Dictionary<string, Entry>>(json, JsonOptions);
            if (legacy is { Count: > 0 })
                return new CredentialFile { Credentials = legacy, DefaultUrl = legacy.Keys.First() };

            return new CredentialFile();
        }
        catch
        {
            // A corrupt or unreadable file should not crash a deploy — treat it as no saved credentials.
            return new CredentialFile();
        }
    }

    private void Write(CredentialFile file)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var json = JsonSerializer.Serialize(file, JsonOptions);
        File.WriteAllText(_filePath, json);

        // Lock the file down to the owner immediately after writing.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private sealed class CredentialFile
    {
        public string? DefaultUrl { get; set; }
        public Dictionary<string, Entry> Credentials { get; set; } = new(StringComparer.Ordinal);
    }

    public sealed record Entry(
        [property: JsonPropertyName("token")] string Token);
}
