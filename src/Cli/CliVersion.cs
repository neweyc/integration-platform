using System.Reflection;

namespace Cli;

// The CLI's own version, for `serto --version`. Read from the assembly metadata stamped at build time
// rather than any hardcoded constant: the publish pipeline supplies the real value via -p:Version (from
// the git tag), and local/dev builds fall back to the csproj placeholder (0.0.0-dev).
public static class CliVersion
{
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var assembly = typeof(CliVersion).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // The SDK appends "+<source-revision-id>" to the informational version on CI builds;
            // trim it so `serto --version` prints a clean "1.2.3" rather than "1.2.3+abc1234".
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
