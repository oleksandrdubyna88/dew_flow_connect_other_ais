namespace CoaiMcp.Runners.Processes;

/// <summary>
/// Whether the process an identity names is still running — THREE answers, because the two-state <see cref="ProcessTracking.StartedAt"/> (<c>null</c> for every failure) is the safe
/// answer for a KILL and the unsafe one for CLEARING something because a process is gone: only <see cref="Gone"/> is evidence of that.
/// </summary>
public abstract record Liveness
{
    private Liveness()
    {
    }

    /// <summary>The pid is the process the identity names, and it has not exited.</summary>
    public sealed record Alive : Liveness;

    /// <summary>It is not running: no such process, an exited (zombie) entry, or the number now belongs to another process.</summary>
    public sealed record Gone(string Why) : Liveness;

    /// <summary>It could not be checked (<c>EACCES</c>, <c>EIO</c>, an unparseable <c>stat</c>): not evidence of anything.</summary>
    public sealed record Unknown(string Reason) : Liveness;
}

/// <summary>The one liveness question — NOT <c>CoaiMcp.Server.ProcessLiveness</c>, the two-state <c>bool IsAlive(int)</c> the sweeps ask, which is untouched.</summary>
public static class LivenessCheck
{
    /// <summary>
    /// Linux: <c>/proc/&lt;pid&gt;</c> absent → <see cref="Liveness.Gone"/>; an unreadable <c>stat</c> → <see cref="Liveness.Unknown"/>; an exited entry → Gone; a start time that is gone or another's →
    /// Gone; else <see cref="Liveness.Alive"/>. Windows and others: no errno exists there, so <c>StartedAt</c> alone decides (markers are written on Linux only).
    /// </summary>
    public static Liveness Of(ProcessIdentity identity) =>
        OperatingSystem.IsLinux() ? Linux(identity, ProcReads.Real, ProcessTracking.StartedAt) : Elsewhere(identity, ProcessTracking.StartedAt);

    /// <summary>The Linux rule over a <c>/proc</c> and a start-time read — the seam, so every answer is a unit test on any platform.</summary>
    internal static Liveness Linux(ProcessIdentity identity, IProcReads reads, Func<int, DateTime?> startedAt) =>
        ProcStat.ReadFor(identity.Pid, reads) switch
        {
            ProcStatRead.Gone => new Liveness.Gone("no such process"),
            ProcStatRead.Unreadable unreadable => new Liveness.Unknown(unreadable.Reason),
            ProcStatRead.Read read when ProcessTree.ExitedEntry(read.Line.State, reads.Threads(identity.Pid), identity.Pid) => new Liveness.Gone("exited (zombie)"),
            _ => Started(identity, startedAt),
        };

    /// <summary>The rule off Linux.</summary>
    internal static Liveness Elsewhere(ProcessIdentity identity, Func<int, DateTime?> startedAt) =>
        startedAt(identity.Pid) is null ? new Liveness.Gone("no such process, or not ours to look at") : Started(identity, startedAt);

    private static Liveness Started(ProcessIdentity identity, Func<int, DateTime?> startedAt) => startedAt(identity.Pid) switch
    {
        null => new Liveness.Gone("exited"),
        { } started when !OrphanSweep.Same(started, identity.StartedUtc) => new Liveness.Gone($"the pid is reused — started at {started:O}"),
        _ => new Liveness.Alive(),
    };
}
