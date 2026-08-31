namespace CoaiMcp.Runners.Processes;

/// <summary>
/// Turning a bare command name into something <see cref="System.Diagnostics.Process"/> can start.
/// </summary>
/// <remarks>
/// <para><b>Found by the second real run (2026-08-31).</b> Every reviewer vendor here installs
/// through npm, and on Windows npm writes THREE shims: an extensionless shell script, a
/// <c>.cmd</c> and a <c>.ps1</c>. A shell resolves `codex` to `codex.cmd` through <c>PATHEXT</c>;
/// <c>Process.Start</c> with <c>UseShellExecute=false</c> does not — it looks for a file called
/// exactly `codex`, finds the extensionless SHELL script, and fails with "The system cannot find
/// the file specified". Which is why `which codex` on this machine answers a path that cannot be
/// executed by the very code that was about to run it.</para>
/// <para>So the resolution is ours: walk <c>PATH</c>, try the name as given and then each
/// <c>PATHEXT</c> suffix, in that order. On POSIX the name is returned unchanged — there is no
/// such gap there.</para>
/// <para>Pure apart from the file probe, and the probe is injected, so the whole table is a unit
/// test rather than something discovered on somebody's laptop.</para>
/// </remarks>
public static class ExecutableResolver
{
    /// <summary>The order Windows itself uses; the executable ones only — a `.py` shim is not ours to start.</summary>
    private static readonly string[] WindowsExtensions = [".exe", ".cmd", ".bat", ".com"];

    /// <summary>
    /// The path to start, or the name unchanged when nothing better was found — a caller that
    /// still fails then gets the OS's own message about the name the person configured.
    /// </summary>
    public static string Resolve(
        string command,
        bool isWindows,
        string? pathVariable,
        Func<string, bool> fileExists)
    {
        // An explicit path is the person's decision; never second-guess it.
        if (!isWindows || command.Contains('/') || command.Contains('\\'))
        {
            return command;
        }

        // Extensions FIRST, the bare name last: on Windows an extensionless file next to a `.cmd`
        // is npm's shell script, which Process.Start cannot execute — trying it first is exactly
        // the bug this class exists to fix, and the first version of it made that mistake.
        var candidates = Path.HasExtension(command)
            ? [command]
            : WindowsExtensions.Select(e => command + e).Append(command);

        foreach (var directory in (pathVariable ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var candidate in candidates)
            {
                var full = Path.Combine(directory, candidate);
                if (fileExists(full))
                {
                    return full;
                }
            }
        }

        return command;
    }

    /// <summary>The same, against this machine.</summary>
    public static string Resolve(string command) =>
        Resolve(command, OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable("PATH"), File.Exists);
}
