using RuntimeAgent.Agent;

namespace RuntimeAgent.Execution;

// A fully-resolved command for launching an integration process in a given working directory.
public sealed record RuntimeLaunchSpec(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);

// Resolves how to launch a non-.NET runtime. Kept behind an interface so the subprocess runner is
// testable without real interpreters: a test supplies its own resolver (e.g. /bin/sh + a script).
public interface IRuntimeLaunchResolver
{
    bool Supports(string? runtime);
    RuntimeLaunchSpec? Resolve(string runtime, string workingDirectory);
}

// Resolves launch commands from AgentOptions.Runtimes. Runtime lookup is case-insensitive.
public sealed class OptionsRuntimeLaunchResolver(AgentOptions options) : IRuntimeLaunchResolver
{
    private readonly Dictionary<string, RuntimeLaunchOptions> _runtimes =
        new(options.Runtimes, StringComparer.OrdinalIgnoreCase);

    public bool Supports(string? runtime) =>
        !string.IsNullOrEmpty(runtime) && _runtimes.ContainsKey(runtime);

    public RuntimeLaunchSpec? Resolve(string runtime, string workingDirectory)
    {
        if (!_runtimes.TryGetValue(runtime, out var launch) || string.IsNullOrWhiteSpace(launch.Command))
            return null;

        return new RuntimeLaunchSpec(
            ResolveCommand(launch.Command, workingDirectory),
            launch.Args,
            workingDirectory);
    }

    // A relative command (e.g. a Go binary shipped in the package) resolves against the working
    // directory when a file is actually there; otherwise the command is left as-is to resolve from PATH.
    private static string ResolveCommand(string command, string workingDirectory)
    {
        if (Path.IsPathRooted(command))
            return command;

        var candidate = Path.Combine(workingDirectory, command);
        return File.Exists(candidate) ? candidate : command;
    }
}
