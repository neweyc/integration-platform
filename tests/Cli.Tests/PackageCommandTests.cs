using Cli.Commands;

namespace Cli.Tests;

public class PackageCommandTests
{
    [Fact]
    public async Task ComputeSha256Async_ReturnsLowercaseHexHash()
    {
        using var file = new TemporaryFile("hello");

        var hash = await PackageCommand.ComputeSha256Async(file.Path);

        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", hash);
    }

    private sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile(string contents)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            File.WriteAllText(Path, contents);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
    }
}
