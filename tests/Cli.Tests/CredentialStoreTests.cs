using Cli;

namespace Cli.Tests;

public class CredentialStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _path;

    public CredentialStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "serto-creds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _path = Path.Combine(_tempDir, "credentials.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Save_ThenGet_ReturnsToken()
    {
        var store = new CredentialStore(_path);
        store.Save("http://localhost:5000", "pat_abc");

        Assert.Equal("pat_abc", store.GetToken("http://localhost:5000"));
    }

    [Fact]
    public void Get_UnknownUrl_ReturnsNull()
    {
        var store = new CredentialStore(_path);
        store.Save("http://localhost:5000", "pat_abc");

        Assert.Null(store.GetToken("https://other.example.com"));
    }

    [Theory]
    [InlineData("http://localhost:5000/")]   // trailing slash
    [InlineData("http://LOCALHOST:5000")]    // case
    [InlineData("  http://localhost:5000 ")] // whitespace
    public void Get_NormalizesUrl(string lookupUrl)
    {
        var store = new CredentialStore(_path);
        store.Save("http://localhost:5000", "pat_abc");

        Assert.Equal("pat_abc", store.GetToken(lookupUrl));
    }

    [Fact]
    public void Save_OverwritesExistingTokenForSameUrl()
    {
        var store = new CredentialStore(_path);
        store.Save("http://localhost:5000", "pat_old");
        store.Save("http://localhost:5000/", "pat_new");

        Assert.Equal("pat_new", store.GetToken("http://localhost:5000"));
        Assert.Single(store.ListUrls());
    }

    [Fact]
    public void DifferentUrls_AreStoredIndependently()
    {
        var store = new CredentialStore(_path);
        store.Save("http://localhost:5000", "pat_local");
        store.Save("https://cp.acme.com", "pat_prod");

        Assert.Equal("pat_local", store.GetToken("http://localhost:5000"));
        Assert.Equal("pat_prod", store.GetToken("https://cp.acme.com"));
    }

    [Fact]
    public void Remove_DeletesTokenAndReturnsTrue()
    {
        var store = new CredentialStore(_path);
        store.Save("http://localhost:5000", "pat_abc");

        Assert.True(store.Remove("http://localhost:5000"));
        Assert.Null(store.GetToken("http://localhost:5000"));
    }

    [Fact]
    public void Remove_UnknownUrl_ReturnsFalse()
    {
        var store = new CredentialStore(_path);

        Assert.False(store.Remove("http://localhost:5000"));
    }

    [Fact]
    public void Clear_RemovesAllCredentials()
    {
        var store = new CredentialStore(_path);
        store.Save("http://localhost:5000", "pat_a");
        store.Save("https://cp.acme.com", "pat_b");

        store.Clear();

        Assert.Empty(store.ListUrls());
    }

    [Fact]
    public void Save_SetsDefaultUrl()
    {
        var store = new CredentialStore(_path);
        store.Save("https://cp.acme.com", "pat_prod");

        Assert.Equal("https://cp.acme.com", store.GetDefaultUrl());
    }

    [Fact]
    public void MostRecentLogin_BecomesDefault()
    {
        var store = new CredentialStore(_path);
        store.Save("http://localhost:5000", "pat_local");
        store.Save("https://cp.acme.com", "pat_prod");

        Assert.Equal("https://cp.acme.com", store.GetDefaultUrl());
    }

    [Fact]
    public void RemovingDefault_RepointsToRemaining()
    {
        var store = new CredentialStore(_path);
        store.Save("http://localhost:5000", "pat_local");
        store.Save("https://cp.acme.com", "pat_prod"); // default

        store.Remove("https://cp.acme.com");

        Assert.Equal("http://localhost:5000", store.GetDefaultUrl());
    }

    [Fact]
    public void RemovingLastCredential_ClearsDefault()
    {
        var store = new CredentialStore(_path);
        store.Save("https://cp.acme.com", "pat_prod");

        store.Remove("https://cp.acme.com");

        Assert.Null(store.GetDefaultUrl());
    }

    [Fact]
    public void GetDefaultUrl_NoCredentials_ReturnsNull()
    {
        var store = new CredentialStore(_path);

        Assert.Null(store.GetDefaultUrl());
    }

    [Fact]
    public void Get_MissingFile_ReturnsNull()
    {
        var store = new CredentialStore(Path.Combine(_tempDir, "does-not-exist.json"));

        Assert.Null(store.GetToken("http://localhost:5000"));
    }

    [Fact]
    public void Get_CorruptFile_ReturnsNullInsteadOfThrowing()
    {
        File.WriteAllText(_path, "{ not valid json");
        var store = new CredentialStore(_path);

        Assert.Null(store.GetToken("http://localhost:5000"));
    }

    [Fact]
    public void Save_WritesOwnerOnlyFileOnUnix()
    {
        if (OperatingSystem.IsWindows())
            return; // Unix file modes are not applicable on Windows.

        var store = new CredentialStore(_path);
        store.Save("http://localhost:5000", "pat_abc");

        var mode = File.GetUnixFileMode(_path);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }
}
