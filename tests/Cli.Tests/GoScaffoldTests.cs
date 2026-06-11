using Cli.Commands;
using Xunit;

namespace Cli.Tests;

public class GoScaffoldTests
{
    [Fact]
    public void ScaffoldGo_WritesContainerManifestAndSources()
    {
        var dir = Path.Combine(Path.GetTempPath(), "serto-go-scaffold-" + Guid.NewGuid().ToString("N"));

        try
        {
            InitCommand.ScaffoldGo(dir, "gohello");

            Assert.True(File.Exists(Path.Combine(dir, "main.go")));
            Assert.True(File.Exists(Path.Combine(dir, "go.mod")));
            Assert.True(File.Exists(Path.Combine(dir, "Dockerfile")));
            Assert.True(File.Exists(Path.Combine(dir, "serto.json")));

            // The generated manifest must be a valid container manifest the CLI can read and preview.
            var manifest = ManifestPackaging.Read(Path.Combine(dir, "serto.json"));
            Assert.Equal("container", manifest.Runtime);
            var integration = Assert.Single(manifest.Integrations);
            Assert.Contains("gohello", integration.Entrypoint); // entrypoint is the image reference

            var scan = ManifestPackaging.ToScanResult(manifest);
            Assert.True(scan.IsValid);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
