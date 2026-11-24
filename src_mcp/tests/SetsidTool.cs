namespace CoaiMcp.Tests;

/// <summary>
/// Whether this machine has a <c>setsid</c> program, and so which road the detached launcher records when its <c>setsid</c> road is asked for. Linux has it (util-linux); macOS does not, and
/// the launcher then falls to <c>sh-nohup</c> — by design (<c>DetachedLauncher</c>, road 3), so a test that asks for the <c>setsid</c> road names the road THIS machine can take.
/// </summary>
internal static class SetsidTool
{
    private static readonly Lazy<bool> Found = new(() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(folder => File.Exists(Path.Combine(folder, "setsid"))));

    /// <summary>True when <c>setsid</c> is on the PATH.</summary>
    internal static bool Exists => Found.Value;

    /// <summary>The road the launcher records for a request of the <c>setsid</c> road: <c>setsid</c> where the program is there, <c>sh-nohup</c> where it falls through.</summary>
    internal static string Road => Exists ? "setsid" : "sh-nohup";
}
