using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
    [GeneratedRegex(@"\A(?<host>[^@\s]+)@(?<os>win32|linux|darwin):(?<pid>\d+)(?::\d+)?\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
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

    /// <summary>
    /// What a sweeper does with a claim it reads on <paramref name="thisHost"/> / <paramref name="thisOs"/>: the whole rule in one place. A claim written on
    /// ANOTHER host or operating system is never judged dead (a pid means something only where it was written, so the process lookup is not even asked);
    /// a claim that does not parse is kept; otherwise <see cref="Verdict"/> decides from the lookup.
    /// </summary>
    /// <param name="claim">The claim file's text.</param>
    /// <param name="thisHost">This machine's name, as the claim recorded it (compared without regard to case).</param>
    /// <param name="thisOs">This machine's operating system in <see cref="NodeSpelling"/>'s words.</param>
    /// <param name="processIsAlive">Asks THIS machine whether a pid runs — <see cref="ProcessIsAlive(int)"/> in real use.</param>
    public static ClaimVerdict Judge(string claim, string thisHost, string thisOs, Func<int, bool> processIsAlive)
    {
        var match = Shape().Match(claim.Trim());
        if (!match.Success || !OnThisMachine(match, thisHost, thisOs)
            || !int.TryParse(match.Groups["pid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
        {
            return ClaimVerdict.Keep;
        }

        return Verdict(claim, processIsAlive(pid), liveStartUnixSeconds: null);
    }

    private static bool OnThisMachine(Match claim, string thisHost, string thisOs) =>
        string.Equals(claim.Groups["host"].Value, thisHost, StringComparison.OrdinalIgnoreCase)
        && string.Equals(claim.Groups["os"].Value, thisOs, StringComparison.Ordinal);

    /// <summary>Whether a pid on THIS machine runs, asking the operating system.</summary>
    public static bool ProcessIsAlive(int pid) =>
        ProcessIsAlive(pid, static candidate =>
        {
            using var process = Process.GetProcessById(candidate);
            if (process.HasExited)
            {
                throw new InvalidOperationException("the process ended");
            }
        });

    /// <summary>
    /// The three outcomes of a lookup are three different facts, and only two mean "gone": <see cref="ArgumentException"/> (no such pid) and
    /// <see cref="InvalidOperationException"/> (it ended between the lookup and the question) are dead; <see cref="Win32Exception"/> (access denied, a
    /// protected process) is ALIVE — being refused an answer is not being told the process ended. Any other failure propagates. (The rule of the benchmark's
    /// <c>WorkerLiveness.ProcessIsAlive</c>; Node's twin is: only <c>ESRCH</c> is dead, <c>EPERM</c> is alive.)
    /// </summary>
    /// <param name="pid">The process to ask about.</param>
    /// <param name="probe">Throws as <see cref="Process.GetProcessById(int)"/> and <c>HasExited</c> do; returns when the process is there.</param>
    public static bool ProcessIsAlive(int pid, Action<int> probe)
    {
        try
        {
            probe(pid);

            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return true;
        }
    }

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
