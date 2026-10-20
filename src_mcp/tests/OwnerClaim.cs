using System.Text.RegularExpressions;

namespace CoaiMcp.Tests;

/// <summary>What a sweeper does with a run folder after reading its owner claim.</summary>
public enum ClaimVerdict
{
    /// <summary>Not provably dead: the folder stays. A leak is the price, never a deletion.</summary>
    Keep,

    /// <summary>The claim parses and its process is gone: the folder may be removed.</summary>
    Leftover,
}

/// <summary>
/// The family's owner claim, <c>&lt;host&gt;@&lt;os&gt;:&lt;pid&gt;[:&lt;startUnixSeconds&gt;]</c>: the ONE writer's text and the ONE judge's table
/// (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>, D2 and the risk consult for epic 2).
/// </summary>
/// <remarks>
/// <para><b>The <c>&lt;os&gt;</c> word is Node's <c>process.platform</c> spelling</b> — <c>win32</c>, <c>linux</c>, <c>darwin</c> — because the reader E5 builds
/// is the conventions' Node sweeper. If its helper on the conventions <c>main</c> spells it differently when it lands, change
/// <c>RunTempTests</c>'s regex and <see cref="NodeSpelling"/> together: those are the two places.</para>
/// <para>The writer and the judge live here, in the test tree, until E5's story S3 adopts both — never two writers.</para>
/// </remarks>
public static partial class OwnerClaim
{
    [GeneratedRegex(@"\A[^@\s]+@(?:win32|linux|darwin):\d+(?::\d+)?\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Shape();

    /// <summary>
    /// What a sweeper may do with the folder this claim guards.
    /// </summary>
    /// <param name="claim">The claim file's text.</param>
    /// <param name="pidAlive">Whether a process with the claim's pid exists now.</param>
    /// <param name="liveStartUnixSeconds">
    /// When that live process started, when the OS gives it. It can NOT change the verdict today: a live pid is kept whatever its start says, because a
    /// live pid with an OLDER recorded start is a REUSED pid — a leak, never a deletion. It stays in the signature so the call has the shape E5's
    /// sweeper will use once a start can also CONFIRM an owner.
    /// </param>
    public static ClaimVerdict Verdict(string claim, bool pidAlive, long? liveStartUnixSeconds) =>
        Shape().IsMatch(claim.Trim()) && !pidAlive ? ClaimVerdict.Leftover : ClaimVerdict.Keep;

    /// <summary>The claim's text — one line, no trailing newline; the start field only when it is given.</summary>
    public static string Format(string host, string os, int pid, long? startUnixSeconds) =>
        startUnixSeconds is { } start ? $"{host}@{os}:{pid}:{start}" : $"{host}@{os}:{pid}";

    /// <summary>The claim of THIS process: the start only on Linux, where .NET reads it from <c>/proc</c> without spawning anything.</summary>
    public static string ForThisProcess() =>
        Format(Sanitised(Environment.MachineName), NodeSpelling(), Environment.ProcessId, OperatingSystem.IsLinux() ? OwnStart() : null);

    /// <summary>The operating system as Node's <c>process.platform</c> says it.</summary>
    public static string NodeSpelling() =>
        OperatingSystem.IsWindows() ? "win32" : OperatingSystem.IsMacOS() ? "darwin" : "linux";

    /// <summary>A host name cannot hold the claim's own separators or whitespace.</summary>
    private static string Sanitised(string host) =>
        Regex.Replace(host, @"[@:\s]", "-", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>This process's start as unix seconds, or none when the OS would not say.</summary>
    private static long? OwnStart()
    {
        try
        {
            using var self = System.Diagnostics.Process.GetCurrentProcess();

            return new DateTimeOffset(self.StartTime).ToUnixTimeSeconds();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }
}
