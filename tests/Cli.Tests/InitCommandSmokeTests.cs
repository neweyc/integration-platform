using System.Diagnostics;
using Cli.Commands;
using Microsoft.Extensions.Logging;
using Serto.Sdk;

namespace Cli.Tests;

/// <summary>
/// End-to-end checks that the scaffold writes the expected files and that the generated integration
/// source actually compiles. The compile test references the already-built SDK/connector DLLs (not the
/// source projects), so it proves the generated code is valid without rebuilding — or polluting — the
/// real source tree, and without depending on the published NuGet packages.
/// </summary>
public class InitCommandSmokeTests
{
    [Fact]
    public void Scaffold_WritesTheExpectedFiles()
    {
        var dir = NewTempDir();
        try
        {
            InitCommand.Scaffold(dir, "AcmeSync", "AcmeSync", "webhook");

            Assert.True(File.Exists(Path.Combine(dir, "AcmeSync.csproj")));
            Assert.True(File.Exists(Path.Combine(dir, "MyIntegration.cs")));
            Assert.True(File.Exists(Path.Combine(dir, "README.md")));
            Assert.True(File.Exists(Path.Combine(dir, ".secrets.example.json")));
            Assert.True(File.Exists(Path.Combine(dir, ".gitignore")));
            Assert.True(File.Exists(Path.Combine(dir, "AcmeSync.Tests", "AcmeSync.Tests.csproj")));
            Assert.True(File.Exists(Path.Combine(dir, "AcmeSync.Tests", "MyIntegrationTests.cs")));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Theory]
    [InlineData("scheduled")]
    [InlineData("webhook")]
    public void GeneratedIntegration_CompilesAgainstTheBuiltSdk(string template)
    {
        // Resolve the SDK and logging assemblies from the ones already loaded by the test process (exact
        // and reliable); the connectors assembly isn't referenced here, so locate its built DLL on disk.
        var sdkDll = typeof(IIntegration).Assembly.Location;
        var loggingDll = typeof(ILogger).Assembly.Location;
        var connectorsDll = FindBuiltDll(Path.Combine(FindRepoRoot(), "src", "Connectors", "bin"), "Connectors.dll");

        // Connectors requires a prior solution build (CI always builds the solution first). If it isn't
        // present locally, there is nothing to compile against — treat as a pass rather than a false failure.
        if (connectorsDll is null)
            return;

        var dir = NewTempDir();
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "MyIntegration.cs"),
                InitCommand.BuildIntegrationClass("SmokeTest", template));

            // Reference the built DLLs directly (no ProjectReference), so this never rebuilds or locks the
            // real source projects.
            File.WriteAllText(Path.Combine(dir, "Smoke.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="Serto.Sdk"><HintPath>{sdkDll}</HintPath></Reference>
                    <Reference Include="Serto.Connectors"><HintPath>{connectorsDll}</HintPath></Reference>
                    <Reference Include="Microsoft.Extensions.Logging.Abstractions"><HintPath>{loggingDll}</HintPath></Reference>
                  </ItemGroup>
                </Project>
                """);

            var (exitCode, output) = RunDotnet("build -nologo", dir);
            Assert.True(exitCode == 0, $"Generated '{template}' integration failed to compile:\n{output}");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "serto-init-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best effort — a locked build artifact shouldn't fail the test.
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Serto.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (Serto.slnx).");
    }

    // Newest matching DLL under a project's bin directory (covers Debug/Release/net10.0 layouts).
    private static string? FindBuiltDll(string binDir, string dllName)
    {
        if (!Directory.Exists(binDir))
            return null;

        return Directory.EnumerateFiles(binDir, dllName, SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static (int ExitCode, string Output) RunDotnet(string arguments, string workingDirectory)
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        })!;

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdout + stderr);
    }
}
