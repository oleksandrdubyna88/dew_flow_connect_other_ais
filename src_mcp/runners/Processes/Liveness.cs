using System.Diagnostics;

namespace CoaiMcp.Runners.Processes;

/// <summary>
/// Whether the process an identity names is still running — THREE answers, because the two-state <see cref="ProcessTracking.StartedAt"/> (<c>null</c> for every failure) is the safe
/// answer for a KILL and the unsafe one for CLEARING something because a process is gone: only <see cref="Gone"/> is evidence of that, and it must be PROVEN.
/// </summary>
/// <remarks>
/// <see cref="Gone"/> is proven by one of three things only: <c>ENOENT</c> on <c>/proc/&lt;pid&gt;</c>, a start time that is another process's, or an exited (zombie) entry. A start time that
/// merely cannot be read proves nothing. <b>The limit of the first proof:</b> <c>ENOENT</c> means "gone" only inside the SAME pid namespace as the process and when <c>/proc</c> is not mounted with
/// <c>hidepid=invisible</c> (<c>hidepid=2</c>) — in another namespace the number names nothing here, and under <c>hidepid</c> another account's process is hidden as if it did not exist. A caller that
/// cannot rule those out (a marker written by a process in a container) must not read <see cref="Gone"/> as proof.
/// </remarks>
public abstract record Liveness
{
    private Liveness()
    {
    }

    /// <summary>The pid is the process the identity names, and it has not exited.</summary>
    public sealed record Alive : Liveness;

    /// <summary>It is not running: no such process, an exited (zombie) entry, or the number now belongs to another process.</summary>
    public sealed record Gone(string Why) : Liveness;

    /// <summary>It could not be checked (<c>EACCES</c>, <c>EIO</c>, an unparseable <c>stat</c>, a start time nobody could read): not evidence of anything.</summary>
    public sealed record Unknown(string Reason) : Liveness;
}

/// <summary>The one liveness question — NOT <c>CoaiMcp.Server.ProcessLiveness</c>, the two-state <c>bool IsAlive(int)</c> the sweeps ask, which is untouched.</summary>
public static class LivenessCheck
{
    /// <summary>
    /// Linux: <c>/proc/&lt;pid&gt;</c> absent → <see cref="Liveness.Gone"/>; an unreadable <c>stat</c> → <see cref="Liveness.Unknown"/>; an exited entry → Gone; a start time that is another's → Gone; a start
    /// time that cannot be read → the <c>stat</c> is read again, and the answer is Gone only if that read now proves it, else <see cref="Liveness.Unknown"/>; else <see cref="Liveness.Alive"/>.
    /// Elsewhere: no errno exists there, so an unreadable start time is Gone only when the process is provably absent (see <see cref="Elsewhere"/>), else Unknown. See the limit in <see cref="Liveness"/>'s remarks.
    /// </summary>
    public static Liveness Of(ProcessIdentity identity) =>
        OperatingSystem.IsLinux() ? Linux(identity, ProcReads.Real, ProcessTracking.StartedAt) : Elsewhere(identity, ProcessTracking.StartedAt, ProvablyAbsent);

    /// <summary>
    /// As <see cref="Of(ProcessIdentity)"/> with the process's EXACT start ticks (field 22 of its <c>stat</c>, as <see cref="ProcStatLine.StartTicks"/> read them when the identity was made): one read of the
    /// line, one exact comparison — no clock and no slack. <paramref name="startTicks"/> 0 (none recorded) falls back to <see cref="Of(ProcessIdentity)"/>; off Linux there are no ticks to compare.
    /// </summary>
    public static Liveness Of(ProcessIdentity identity, ulong startTicks) =>
        OperatingSystem.IsLinux() && startTicks != 0 ? Linux(identity, startTicks, ProcReads.Real, ProcessTracking.StartedAt) : Of(identity);

    /// <summary>The Linux rule over a <c>/proc</c> and a start-time read — the seam, so every answer is a unit test on any platform.</summary>
    internal static Liveness Linux(ProcessIdentity identity, IProcReads reads, Func<int, DateTime?> startedAt) =>
        ProcStat.ReadFor(identity.Pid, reads) switch
        {
            ProcStatRead.Gone => new Liveness.Gone("no such process"),
            ProcStatRead.Unreadable unreadable => new Liveness.Unknown(unreadable.Reason),
            ProcStatRead.Read read when Exited(read, reads) => new Liveness.Gone("exited (zombie)"),
            _ => Started(identity, reads, startedAt),
        };

    /// <summary>
    /// The Linux rule on EXACT start ticks: <c>/proc/&lt;pid&gt;/stat</c> is read ONCE, and its <c>starttime</c> must equal the recorded one. The recorded ticks are read when a member is PINNED, which can be after
    /// it was listed, so the identity's own start time — the listing's — must agree too (within <see cref="OrphanSweep.Same"/>'s slack): a pid reused between the listing and the pin has the ticks just read and a
    /// different start time, and is not signalled.
    /// </summary>
    internal static Liveness Linux(ProcessIdentity identity, ulong startTicks, IProcReads reads, Func<int, DateTime?> startedAt) =>
        ProcStat.ReadFor(identity.Pid, reads) switch
        {
            ProcStatRead.Gone => new Liveness.Gone("no such process"),
            ProcStatRead.Unreadable unreadable => new Liveness.Unknown(unreadable.Reason),
            ProcStatRead.Read read => Exited(read, reads) ? new Liveness.Gone("exited (zombie)") : Confirmed(identity, SameTicks(read.Line.StartTicks, startTicks), reads, startedAt),
            _ => new Liveness.Unknown("an unknown stat read"),
        };

    /// <summary>The ticks agreed: the listing's start time must agree as well. Anything but a match stands as it is.</summary>
    private static Liveness Confirmed(ProcessIdentity identity, Liveness byTicks, IProcReads reads, Func<int, DateTime?> startedAt) =>
        byTicks is Liveness.Alive ? Started(identity, reads, startedAt) : byTicks;

    private static Liveness SameTicks(ulong found, ulong recorded) => found switch
    {
        0 => new Liveness.Unknown("start ticks are not in the stat line"),
        _ when found != recorded => new Liveness.Gone($"the pid is reused — started at tick {found}, not {recorded}"),
        _ => new Liveness.Alive(),
    };

    /// <summary>
    /// The rule off Linux. The start time is read ONCE. When it cannot be, the answer is Gone only if <paramref name="provablyAbsent"/> proves there is no such process (Windows: no process has that id),
    /// else <see cref="Liveness.Unknown"/> — a process another account owns refuses its start time exactly as an absent one does.
    /// </summary>
    internal static Liveness Elsewhere(ProcessIdentity identity, Func<int, DateTime?> startedAt, Func<int, bool> provablyAbsent) => startedAt(identity.Pid) switch
    {
        null when provablyAbsent(identity.Pid) => new Liveness.Gone("no such process"),
        null => new Liveness.Unknown("start time unreadable, and the process is not provably absent"),
        { } started when !OrphanSweep.Same(started, identity.StartedUtc) => new Liveness.Gone($"the pid is reused — started at {started:O}"),
        _ => new Liveness.Alive(),
    };

    /// <summary>The start time was read ONCE; a stranger's start is Gone, and one that could not be read is decided by a second look at the <c>stat</c>, never assumed.</summary>
    private static Liveness Started(ProcessIdentity identity, IProcReads reads, Func<int, DateTime?> startedAt) => startedAt(identity.Pid) switch
    {
        null => Unstarted(identity, reads),
        { } started when !OrphanSweep.Same(started, identity.StartedUtc) => new Liveness.Gone($"the pid is reused — started at {started:O}"),
        _ => new Liveness.Alive(),
    };

    /// <summary>The start time could not be read: the process is Gone only if the <c>stat</c> now says it is gone or exited; otherwise nothing is proven.</summary>
    private static Liveness Unstarted(ProcessIdentity identity, IProcReads reads) => ProcStat.ReadFor(identity.Pid, reads) switch
    {
        ProcStatRead.Gone => new Liveness.Gone("no such process"),
        ProcStatRead.Read read when Exited(read, reads) => new Liveness.Gone("exited (zombie)"),
        _ => new Liveness.Unknown("start time unreadable"),
    };

    private static bool Exited(ProcStatRead.Read read, IProcReads reads) => ProcessTree.ExitedEntry(read.Line.State, reads.Threads(read.Line.Pid), read.Line.Pid);

    /// <summary>True only when no process has this id: <c>Process.GetProcessById</c> answers <see cref="ArgumentException"/> for exactly that, and any other failure proves nothing.</summary>
    private static bool ProvablyAbsent(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);

            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
