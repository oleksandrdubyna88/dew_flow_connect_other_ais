using System.Runtime.Versioning;

namespace CoaiMcp.Runners.Processes;

/// <summary>What one member of a tree being ended answered — a closed union; only <see cref="Sent"/> means a signal went.</summary>
public abstract record Signalled
{
    private Signalled()
    {
    }

    /// <summary><c>SIGKILL</c> was sent through the pinned descriptor.</summary>
    public sealed record Sent(ProcessIdentity Member) : Signalled;

    /// <summary>Not running any more (or the number is another process's): nothing was sent.</summary>
    public sealed record Gone(ProcessIdentity Member, string Why) : Signalled;

    /// <summary>It could not be checked, so nothing was sent: a process that cannot be told from another is not signalled.</summary>
    public sealed record Unknown(ProcessIdentity Member, string Reason) : Signalled;

    /// <summary>The operating system refused a step with this <c>errno</c> (a pid that cannot be pinned for another reason than ESRCH, a signal that was refused).</summary>
    public sealed record Failed(ProcessIdentity Member, int Errno) : Signalled;
}

/// <summary>A member to end: who it is (<see cref="ProcessIdentity"/>) and the exact <c>starttime</c> ticks its <c>/proc</c> line gave when it was listed — 0 when none were recorded, which falls back to the
/// identity's start time.</summary>
public sealed record PinnedMember(ProcessIdentity Identity, ulong StartTicks = 0)
{
    /// <summary>The member with the start ticks <paramref name="reads"/> give for it now; 0 when its <c>stat</c> cannot be read (the identity's start time then decides).</summary>
    public static PinnedMember Of(ProcessIdentity identity, IProcReads reads) =>
        new(identity, ProcStat.ReadFor(identity.Pid, reads) is ProcStatRead.Read { Line.StartTicks: var ticks } ? ticks : 0);
}

/// <summary>
/// Ends members BY IDENTITY: a <c>pidfd</c> pins each process FIRST, its liveness and start are then checked on the pinned process, and the signal goes through the descriptor — so a pid
/// can never be reused between the check and the signal. A signal by NUMBER is never sent.
/// </summary>
/// <remarks>
/// The order per member is wsl_care's (<c>ProcessSignals.cs:192-220</c>, MIT, same owner): pin → check → send. The shell is rewritten on this repository's <see cref="ProcessIdentity"/> (through
/// <see cref="LivenessCheck"/>), not wsl_care's <c>Tuning</c> or <c>IFileSystem</c>. <c>SIGKILL</c> only: the caller's graceful step ran already, and
/// the wait is the caller's re-listing loop, so <see cref="IPidfdCalls.Poll"/> is kept only to tell a pinned process whose <c>stat</c> cannot be read but whose descriptor is readable (it has exited).
/// <para>Two ways to say who a member is. A <see cref="ProcessIdentity"/> carries a start TIME (from <c>Process.StartTime</c>, an estimate from the boot time), so it can only be compared within
/// <see cref="OrphanSweep.Same"/>'s one-second slack. A <see cref="PinnedMember"/> adds the EXACT start ticks of the same <c>/proc</c> line the state is read from: one read, one exact comparison.</para>
/// </remarks>
[SupportedOSPlatform("linux")]
public static class TreeSignals
{
    private const int SigKill = 9;
    private const int NoSuchProcess = 3;

    /// <summary>For each member: pin it, check it on the pin, send <c>SIGKILL</c> through the descriptor, close it. Answers per member, in order. The check is by start TIME, within the identity slack;
    /// pass <see cref="PinnedMember"/>s for the exact one.</summary>
    public static IReadOnlyList<Signalled> Kill(IReadOnlyList<ProcessIdentity> members, IPidfdCalls calls) => Kill(members, calls, LivenessCheck.Of);

    /// <summary><see cref="Kill(IReadOnlyList{ProcessIdentity}, IPidfdCalls)"/> with the liveness question handed in — the seam.</summary>
    internal static IReadOnlyList<Signalled> Kill(IReadOnlyList<ProcessIdentity> members, IPidfdCalls calls, Func<ProcessIdentity, Liveness> liveness) =>
        Kill([.. members.Select(member => new PinnedMember(member))], calls, pinned => liveness(pinned.Identity));

    /// <summary>For each member: pin it, check it on the pin by its EXACT start ticks (the same <c>stat</c> line as its state), send <c>SIGKILL</c> through the descriptor, close it.</summary>
    public static IReadOnlyList<Signalled> Kill(IReadOnlyList<PinnedMember> members, IPidfdCalls calls) => Kill(members, calls, pinned => LivenessCheck.Of(pinned.Identity, pinned.StartTicks));

    /// <summary><see cref="Kill(IReadOnlyList{PinnedMember}, IPidfdCalls)"/> with the liveness question handed in — the seam.</summary>
    internal static IReadOnlyList<Signalled> Kill(IReadOnlyList<PinnedMember> members, IPidfdCalls calls, Func<PinnedMember, Liveness> liveness) =>
        [.. members.Select(member => Pinned(member, calls, liveness))];

    private static Signalled Pinned(PinnedMember pinned, IPidfdCalls calls, Func<PinnedMember, Liveness> liveness)
    {
        var member = pinned.Identity;
        var fd = calls.Open(member.Pid);
        if (fd < 0)
        {
            return -fd == NoSuchProcess ? new Signalled.Gone(member, "no such process") : new Signalled.Failed(member, -fd);
        }

        try
        {
            return Checked(member, fd, calls, liveness(pinned));
        }
        finally
        {
            calls.Close(fd);
        }
    }

    private static Signalled Checked(ProcessIdentity member, int fd, IPidfdCalls calls, Liveness liveness) => liveness switch
    {
        Liveness.Alive => Sent(member, fd, calls),
        Liveness.Gone gone => new Signalled.Gone(member, gone.Why),
        Liveness.Unknown unknown when calls.Poll([fd], new bool[1], 0) > 0 => new Signalled.Gone(member, $"exited (its pidfd is readable; {unknown.Reason})"),
        Liveness.Unknown unknown => new Signalled.Unknown(member, unknown.Reason),
        _ => new Signalled.Unknown(member, "an unknown liveness"),
    };

    private static Signalled Sent(ProcessIdentity member, int fd, IPidfdCalls calls) => calls.Signal(fd, SigKill) switch
    {
        0 => new Signalled.Sent(member),
        NoSuchProcess => new Signalled.Gone(member, "no such process"),
        var errno => new Signalled.Failed(member, errno),
    };
}
