using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cli;

/// <summary>
/// Persists the user's API token between commands so they don't have to re-enter it on every deploy.
/// Tokens are stored per control-plane URL in <c>~/.serto/credentials.json</c>. The file is written with
/// owner-only permissions on Unix (the same approach the GitHub, AWS, and npm CLIs use): the token is a
/// revocable, tenant-scoped credential, so the practical protection is keeping it unreadable by other
/// local users rather than encrypting it at rest.
/// </summary>
public sealed class CredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
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
        var credentials = Load();
        return credentials.TryGetValue(Normalize(controlPlaneUrl), out var entry)
               && !string.IsNullOrWhiteSpace(entry.Token)
            ? entry.Token
            : null;
    }

    public void Save(string controlPlaneUrl, string token)
    {
        var credentials = Load();
        credentials[Normalize(controlPlaneUrl)] = new Entry(token.Trim());
        Write(credentials);
    }

    public bool Remove(string controlPlaneUrl)
    {
        var credentials = Load();
        if (!credentials.Remove(Normalize(controlPlaneUrl)))
            return false;

        Write(credentials);
        return true;
    }

    public void Clear()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }

    public IReadOnlyCollection<string> ListUrls() => Load().Keys.ToList();

    // URLs are matched case-insensitively and ignoring a trailing slash, so "http://Localhost:5000/"
    // and "http://localhost:5000" resolve to the same saved token.
    internal static string Normalize(string controlPlaneUrl) =>
        controlPlaneUrl.Trim().TrimEnd('/').ToLowerInvariant();

    private Dictionary<string, Entry> Load()
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, Entry>(StringComparer.Ordinal);

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, Entry>>(json, JsonOptions)
                   ?? new Dictionary<string, Entry>(StringComparer.Ordinal);
        }
        catch
        {
            // A corrupt or unreadable file should not crash a deploy — treat it as no saved credentials.
            return new Dictionary<string, Entry>(StringComparer.Ordinal);
        }
    }

    private void Write(Dictionary<string, Entry> credentials)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var json = JsonSerializer.Serialize(credentials, JsonOptions);
        File.WriteAllText(_filePath, json);

        // Lock the file down to the owner immediately after writing.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public sealed record Entry(
        [property: JsonPropertyName("token")] string Token);
}
