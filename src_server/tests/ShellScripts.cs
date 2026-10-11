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
    /// A Python 3 a script can call as <c>python3</c>: the one on PATH off Windows; on Windows the first real
    /// <c>python.exe</c> on PATH — never the Store's <c>WindowsApps</c> alias, which prints an advert and exits 9009.
    /// Empty when there is none.
    /// </summary>
    public static string Python()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "python3";
        }

        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(dir => !dir.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
            .Select(dir => Path.Combine(dir, "python.exe"))
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
