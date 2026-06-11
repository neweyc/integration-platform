using Cli.Commands;
using Xunit;

namespace Cli.Tests;

public class NodeScaffoldTests
{
    [Fact]
    public void ScaffoldNode_WritesNodeManifestAndSources()
    {
        var dir = Path.Combine(Path.GetTempPath(), "serto-node-scaffold-" + Guid.NewGuid().ToString("N"));

        try
        {
            InitCommand.ScaffoldNode(dir, "nodehello");

            Assert.True(File.Exists(Path.Combine(dir, "index.js")));
            Assert.True(File.Exists(Path.Combine(dir, "package.json")));
            Assert.True(File.Exists(Path.Combine(dir, "serto.json")));

            var manifest = ManifestPackaging.Read(Path.Combine(dir, "serto.json"));
            Assert.Equal("node", manifest.Runtime);
            var integration = Assert.Single(manifest.Integrations);
            Assert.Equal("index.js#handler", integration.Entrypoint);

            var scan = ManifestPackaging.ToScanResult(manifest);
            Assert.True(scan.IsValid);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
