namespace CoaiServer.Tests;

/// <summary>
/// What a test needs to run one of the repository's shell scripts for real: a bash, a Python 3, paths as bash reads
/// them, and the repository root. Shared by every suite that drives a script (<see cref="TheImageChecksEachInstallerTests"/>,
/// <see cref="TheCanaryFallbackRunsTests"/>) rather than copied into each.
/// </summary>
internal static class ShellScripts
{
    /// <summary>bash on PATH, else Git for Windows' — empty when neither exists.</summary>
    public static string Bash()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "bash";
        }

        var git = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");

        return File.Exists(git) ? git : string.Empty;
    }

    /// <summary>
    /// The ABSOLUTE path of a Python 3 a script can call as <c>python3</c>: the first <c>python3</c> on PATH off
    /// Windows; on Windows the first real <c>python.exe</c> — never the Store's <c>WindowsApps</c> alias, which prints an
    /// advert and exits 9009. Empty when there is none.
    /// </summary>
    /// <remarks>
    /// Absolute, never the bare name: a test puts a <c>python3</c> shim FIRST on PATH that execs this, and a shim
    /// that execs <c>python3</c> finds itself and loops for ever — which is how <c>TheCanaryFallbackRunsTests</c> held
    /// CI's Linux and macOS legs to their 30-minute limit on PR #761.
    /// </remarks>
    public static string Python()
    {
        var name = OperatingSystem.IsWindows() ? "python.exe" : "python3";

        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(dir => !dir.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
            .Select(dir => Path.Combine(dir, name))
            .FirstOrDefault(File.Exists) ?? string.Empty;
    }

    /// <summary>A path as bash and curl read it as an ARGUMENT: forward slashes (Git for Windows takes <c>D:/…</c>).</summary>
    public static string Posix(string path) => path.Replace('\\', '/');

    /// <summary>
    /// A directory as a PATH entry inside bash: <c>/d/…</c> on Git for Windows, where the drive's colon would split
    /// <c>PATH</c>; the path itself elsewhere.
    /// </summary>
    public static string PathEntry(string dir) =>
        OperatingSystem.IsWindows() && dir.Length > 1 && dir[1] == ':'
            ? "/" + char.ToLowerInvariant(dir[0]) + Posix(dir[2..])
            : Posix(dir);

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src_vs_code")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("the repository root was not found from the test binary");
    }
}
