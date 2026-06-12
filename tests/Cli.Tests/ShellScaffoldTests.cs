using Cli.Commands;
using Xunit;

namespace Cli.Tests;

public class ShellScaffoldTests
{
    [Fact]
    public void ScaffoldShell_WritesShellManifestAndScript()
    {
        var dir = Path.Combine(Path.GetTempPath(), "serto-shell-scaffold-" + Guid.NewGuid().ToString("N"));

        try
        {
            InitCommand.ScaffoldShell(dir, "nightly-close");

            Assert.True(File.Exists(Path.Combine(dir, "job.sh")));
            Assert.True(File.Exists(Path.Combine(dir, "serto.json")));

            var manifest = ManifestPackaging.Read(Path.Combine(dir, "serto.json"));
            Assert.Equal("shell", manifest.Runtime);
            var integration = Assert.Single(manifest.Integrations);
            Assert.Equal("sh job.sh", integration.Entrypoint);

            var scan = ManifestPackaging.ToScanResult(manifest);
            Assert.True(scan.IsValid);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
