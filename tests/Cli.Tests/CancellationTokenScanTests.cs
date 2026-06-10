using Cli.Commands;

namespace Cli.Tests;

public class CancellationTokenScanTests
{
    [Fact]
    public void RunAsyncThatIgnoresToken_IsFlagged()
    {
        const string source = """
            public class Job : IIntegration
            {
                public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
                {
                    await Task.Delay(1000);
                }
            }
            """;

        Assert.Equal("ct", TestCommand.DetectUnusedCancellationToken(source));
    }

    [Fact]
    public void RunAsyncThatUsesToken_IsNotFlagged()
    {
        const string source = """
            public class Job : IIntegration
            {
                public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
                {
                    await Task.Delay(1000, ct);
                }
            }
            """;

        Assert.Null(TestCommand.DetectUnusedCancellationToken(source));
    }

    [Fact]
    public void TokenPassedToHelperMethod_CountsAsUsed()
    {
        const string source = """
            public class Job : IIntegration
            {
                public Task RunAsync(IIntegrationContext context, CancellationToken cancellationToken)
                    => DoWorkAsync(cancellationToken);
            }
            """;

        Assert.Null(TestCommand.DetectUnusedCancellationToken(source));
    }

    [Fact]
    public void SourceWithoutRunAsync_ReturnsNull()
    {
        Assert.Null(TestCommand.DetectUnusedCancellationToken("public class Plain { }"));
    }

    [Fact]
    public void DiscoverWarnings_ReportsFileWithUnusedToken()
    {
        using var project = new TempProjectDirectory();
        project.WriteFile("Job.cs", """
            public class Job
            {
                public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
                {
                    await Task.Delay(1000);
                }
            }
            """);

        var warnings = TestCommand.DiscoverCancellationTokenWarnings(project.Path);

        var warning = Assert.Single(warnings);
        Assert.Contains("Job.cs", warning);
        Assert.Contains("ct", warning);
    }

    [Fact]
    public void DiscoverWarnings_IgnoresBinAndObjOutput()
    {
        using var project = new TempProjectDirectory();
        project.WriteFile(Path.Combine("obj", "Generated.cs"), """
            public class Job
            {
                public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
                {
                    await Task.Delay(1000);
                }
            }
            """);

        Assert.Empty(TestCommand.DiscoverCancellationTokenWarnings(project.Path));
    }

    private sealed class TempProjectDirectory : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ct-scan-" + Guid.NewGuid().ToString("N"));

        public TempProjectDirectory() => Directory.CreateDirectory(Path);

        public void WriteFile(string relativePath, string contents)
        {
            var full = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, contents);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
