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
/// <c>RunTempTests</c>'s regex, <c>shared/owner-claim-vectors.json</c> and <see cref="NodeSpelling"/> together: those are the places.</para>
/// <para>The writer and the judge live here, in the test tree, until E5's story S3 adopts both — never two writers. There ARE two WRITERS of the text today (this class and
/// <c>claimText</c> in <c>src_vs_code/scripts/run-tests.mjs</c>); <c>shared/owner-claim-vectors.json</c> is read by a C# test and a Node test so they cannot drift,
/// and <b>E5's reader must pass the same vectors</b>.</para>
/// <para><b>The host is spelled as the OS names it</b> (<see cref="System.Net.Dns.GetHostName"/>, the <c>gethostname</c> libuv's <c>os.hostname()</c> calls), never
/// <c>Environment.MachineName</c> (NetBIOS: upper case, cut at 15 characters, domain dropped on Unix): the Node sweeper compares the host and would otherwise judge every C#
/// claim "another host" and keep it for ever. A reader still compares the host WITHOUT regard to case (<see cref="Judge"/>).</para>
/// <para><b>The Linux start field</b> is computed by one formula on both sides (<see cref="LinuxStartSeconds"/>: <c>/proc/self/stat</c> field 22 ticks ÷ 100 plus the
/// <c>btime</c> of <c>/proc/stat</c>), not by <c>Process.StartTime</c>, which is derived from other clocks and can differ by about a second. Nothing compares the start today
/// (a live pid is always kept); when E5 does, it decides its own precision rule — and a comparison of a start from another source needs a ±2 s tolerance.</para>
/// </remarks>
public static partial class OwnerClaim
{
    [GeneratedRegex(@"\A(?<host>[^@\s]+)@(?<os>win32|linux|darwin):(?<pid>\d+)(?::\d+)?\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Shape();

    /// <summary>
    /// What a sweeper may do with the folder this claim guards, given whether its pid runs.
    /// </summary>
    /// <remarks>
    /// <b>A live pid is ALWAYS kept</b> — a pid that runs but belongs to some other process (a REUSED pid) is a leak, never a deletion. Comparing the claim's recorded
    /// start with the live process's start would tell the two apart, but that is E5's to decide with its own precision rule (see the class remarks); until then no start
    /// takes part, and this method has no parameter for one. A claim that does not parse is kept; a claim that parses and whose pid is gone is a <see cref="ClaimVerdict.Leftover"/>.
    /// </remarks>
    /// <param name="claim">The claim file's text (a trailing newline is tolerated).</param>
    /// <param name="pidAlive">Whether a process with the claim's pid exists now.</param>
    public static ClaimVerdict Verdict(string claim, bool pidAlive) =>
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

        return Verdict(claim, processIsAlive(pid));
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

    /// <summary>Whether <paramref name="text"/> is exactly a claim: no trimming, no trailing newline (<c>shared/owner-claim-vectors.json</c>'s valid / invalid lists).</summary>
    public static bool IsWellFormed(string text) => Shape().IsMatch(text);

    /// <summary>
    /// The Linux start field from the text of <c>/proc/self/stat</c> and <c>/proc/stat</c>: field 22 (ticks of 1/100 s since boot, counted after the LAST ")" because the
    /// command name may hold spaces and ")") divided by 100, plus the <c>btime</c> line. None when either does not parse — no start is better than a wrong one. The same
    /// formula as <c>linuxStartSeconds</c> in <c>run-tests.mjs</c>, pinned by the same vectors.
    /// </summary>
    public static long? LinuxStartSeconds(string stat, string procStat) =>
        StartTicks(stat) is { } ticks && BootSeconds(procStat) is { } boot ? boot + (ticks / 100) : null;

    private static long? StartTicks(string stat)
    {
        var after = stat.LastIndexOf(')') + 2;
        var field = after <= stat.Length ? stat[after..].Split(' ').ElementAtOrDefault(19) : null;

        return long.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ? ticks : null;
    }

    private static long? BootSeconds(string procStat)
    {
        var line = BootLine().Match(procStat);

        return line.Success && long.TryParse(line.Groups["t"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var boot) ? boot : null;
    }

    [GeneratedRegex(@"^btime (?<t>\d+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BootLine();

    /// <summary>The claim's text — one line, no trailing newline; the host made safe (<see cref="Sanitised"/>); the start field only when it is given.</summary>
    public static string Format(string host, string os, int pid, long? startUnixSeconds) =>
        startUnixSeconds is { } start ? $"{Sanitised(host)}@{os}:{pid}:{start}" : $"{Sanitised(host)}@{os}:{pid}";

    /// <summary>The claim of THIS process: the host as the OS names it, the start only on Linux, where it is read from <c>/proc</c> without spawning anything.</summary>
    public static string ForThisProcess() =>
        Format(System.Net.Dns.GetHostName(), NodeSpelling(), Environment.ProcessId, OperatingSystem.IsLinux() ? OwnStart() : null);

    /// <summary>The operating system as Node's <c>process.platform</c> says it.</summary>
    public static string NodeSpelling() =>
        OperatingSystem.IsWindows() ? "win32" : OperatingSystem.IsMacOS() ? "darwin" : "linux";

    /// <summary>A host name cannot hold the claim's own separators or whitespace: each becomes "-".</summary>
    public static string Sanitised(string host) =>
        Regex.Replace(host, @"[@:\s]", "-", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>This process's start as unix seconds by <see cref="LinuxStartSeconds"/>, or none when <c>/proc</c> cannot be read.</summary>
    private static long? OwnStart()
    {
        try
        {
            return LinuxStartSeconds(File.ReadAllText("/proc/self/stat"), File.ReadAllText("/proc/stat"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
