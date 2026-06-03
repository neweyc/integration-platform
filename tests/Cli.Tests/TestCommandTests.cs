using Cli.Commands;

namespace Cli.Tests;

public class TestCommandTests
{
    [Fact]
    public async Task LoadSecretsAsync_ReturnsEmptyDictionaryWhenPathMissing()
    {
        var secrets = await TestCommand.LoadSecretsAsync(null);

        Assert.Empty(secrets);
    }

    [Fact]
    public async Task LoadSecretsAsync_LoadsJsonSecrets()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".json");
        await File.WriteAllTextAsync(path, """
{
  "ApiKey": "secret-value",
  "Tenant": "acme"
}
""");

        try
        {
            var secrets = await TestCommand.LoadSecretsAsync(path);

            Assert.Equal("secret-value", secrets["ApiKey"]);
            Assert.Equal("acme", secrets["Tenant"]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadSecretsAsync_ThrowsWhenFileDoesNotExist()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".json");

        await Assert.ThrowsAsync<FileNotFoundException>(() => TestCommand.LoadSecretsAsync(path));
    }
}
